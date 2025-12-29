using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class Autosaving
{
    public static void DoAutosave()
    {
        LongEventHandler.QueueLongEvent(() =>
        {
            SaveGameToFile_Overwrite(GetNextAutosaveFileName(), false);
            Multiplayer.Client.Send(Packets.Client_Autosaving);
        }, "MpSaving", false, null);
    }

    private static string GetNextAutosaveFileName()
    {
        var autosavePrefix = "Autosave-";

        if (Multiplayer.settings.appendNameToAutosave)
            autosavePrefix += $"{GenFile.SanitizedFileName(Multiplayer.session.gameName)}-";

        return Enumerable
            .Range(1, Multiplayer.settings.autosaveSlots)
            .Select(i => $"{autosavePrefix}{i}")
            .OrderBy(s => new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{s}.zip")).LastWriteTime)
            .First();
    }

    public static void SaveGameToFile_Overwrite(string fileNameNoExtension, bool currentReplay)
    {
        Log.Message($"Multiplayer: saving to file {fileNameNoExtension}");

        try
        {
            // Ensure the replays directory exists even when not connected to a server
            Directory.CreateDirectory(Multiplayer.ReplaysDir);

            var tmp = new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.tmp.zip"));
            Replay.ForSaving(tmp).WriteData(
                currentReplay ?
                    Multiplayer.session.dataSnapshot :
                    SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveGameData(), false)
            );

            var dst = new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.zip"));
            if (!dst.Exists) dst.Open(FileMode.Create).Close();
            tmp.Replace(dst.FullName, null);

            Messages.Message("MpGameSaved".Translate(fileNameNoExtension), MessageTypeDefOf.SilentInput, false);
            // In bootstrap/offline mode there may be no active session
            if (Multiplayer.session != null)
                Multiplayer.session.lastSaveAt = Time.realtimeSinceStartup;
        }
        catch (Exception e)
        {
            Log.Error($"Exception saving multiplayer game as {fileNameNoExtension}: {e}");
            Messages.Message("MpGameSaveFailed".Translate(), MessageTypeDefOf.SilentInput, false);
        }
    }

    /// <summary>
    /// Debug helper: try multiple save strategies to compare outputs.
    /// Generates three zips: {baseName}-snap.zip, {baseName}-snap_nocurmap.zip, {baseName}-manual.zip
    /// </summary>
    public static void SaveVanillaGameDebugVariants(string baseName)
    {
        try
        {
            Log.Message($"Bootstrap-debug: starting multi-variant save for {baseName}");

            // Variant 1: normal snapshot
            try
            {
                // Use the standard MP save pipeline for the snap variant
                SaveGameToFile_Overwrite(baseName + "-snap", currentReplay: false);
            }
            catch (Exception e)
            {
                Log.Error($"Bootstrap-debug: snap variant failed: {e}");
            }

            // Variant 2: snapshot with currentMapIndex removed
            try
            {
                Directory.CreateDirectory(Multiplayer.ReplaysDir);

                var tmpData = SaveLoad.SaveGameData();
                var snapshot = SaveLoad.CreateGameDataSnapshot(tmpData, removeCurrentMapId: true);
                WriteSnapshotToReplay(snapshot, baseName + "-snap_nocurmap");
            }
            catch (Exception e)
            {
                Log.Error($"Bootstrap-debug: snap_nocurmap variant failed: {e}");
            }

            // Variant 3: manual extraction only
            try
            {
                Directory.CreateDirectory(Multiplayer.ReplaysDir);

                var gameDoc = SaveLoad.SaveGameToDoc();
                var tempData = new TempGameData(gameDoc, Array.Empty<byte>());
                var snapshot = ExtractSnapshotManually(tempData);
                WriteSnapshotToReplay(snapshot, baseName + "-manual");
            }
            catch (Exception e)
            {
                Log.Error($"Bootstrap-debug: manual variant failed: {e}");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Bootstrap-debug: fatal error during multi-variant save: {e}");
        }
    }

    private static GameDataSnapshot ExtractSnapshotManually(TempGameData tempData)
    {
        var root = tempData.SaveData.DocumentElement;
        var gameNode = root != null ? root["game"] : null;
        var mapsNode = gameNode != null ? gameNode["maps"] : null;

        Log.Message($"Bootstrap-manual: document has gameNode={gameNode != null}, mapsNode={mapsNode != null}, mapsNode.ChildNodes.Count={mapsNode?.ChildNodes.Count ?? -1}");

        var mapDataDict = new System.Collections.Generic.Dictionary<int, byte[]>();
        var mapCmdsDict = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScheduledCommand>>();

        if (mapsNode != null)
        {
            foreach (System.Xml.XmlNode mapNode in mapsNode)
            {
                var idNode = mapNode?["uniqueID"];
                if (idNode == null)
                {
                    Log.Warning($"Bootstrap-manual: skipping map node without uniqueID");
                    continue;
                }
                int id = int.Parse(idNode.InnerText);
                mapDataDict[id] = ScribeUtil.XmlToByteArray(mapNode);
                mapCmdsDict[id] = new System.Collections.Generic.List<ScheduledCommand>();
                Log.Message($"Bootstrap-manual: extracted map {id} ({mapDataDict[id].Length} bytes)");
            }
            mapsNode.RemoveAll();
        }

        mapCmdsDict[ScheduledCommand.Global] = new System.Collections.Generic.List<ScheduledCommand>();

        var gameBytes = ScribeUtil.XmlToByteArray(tempData.SaveData);
        Log.Message($"Bootstrap-manual: manual snapshot extracted: maps={mapDataDict.Count}, world bytes={gameBytes.Length}");
        return new GameDataSnapshot(TickPatch.Timer, gameBytes, tempData.SessionData, mapDataDict, mapCmdsDict);
    }

    private static void WriteSnapshotToReplay(GameDataSnapshot snapshot, string fileNameNoExtension)
    {
        var zipPath = Path.Combine(Multiplayer.ReplaysDir, fileNameNoExtension + ".zip");
        var tmpZipPath = Path.Combine(Multiplayer.ReplaysDir, fileNameNoExtension + ".tmp.zip");

        if (File.Exists(tmpZipPath))
        {
            try { File.Delete(tmpZipPath); } catch (Exception delEx) { Log.Warning($"Bootstrap-debug: couldn't delete existing tmp zip {tmpZipPath}: {delEx}"); }
        }

        using (var replayZip = MpZipFile.Open(tmpZipPath, ZipArchiveMode.Create))
        {
            const string sectionIdStr = "000";

            replayZip.AddEntry($"world/{sectionIdStr}_save", snapshot.GameData);

            if (!snapshot.MapCmds.TryGetValue(ScheduledCommand.Global, out var worldCmds))
                worldCmds = new System.Collections.Generic.List<ScheduledCommand>();
            replayZip.AddEntry($"world/{sectionIdStr}_cmds", ScheduledCommand.SerializeCmds(worldCmds));

            int writtenMaps = 0;
            foreach (var kv in snapshot.MapData)
            {
                int mapId = kv.Key;
                byte[] mapData = kv.Value;
                replayZip.AddEntry($"maps/{sectionIdStr}_{mapId}_save", mapData);

                if (!snapshot.MapCmds.TryGetValue(mapId, out var cmds))
                    cmds = new System.Collections.Generic.List<ScheduledCommand>();
                replayZip.AddEntry($"maps/{sectionIdStr}_{mapId}_cmds", ScheduledCommand.SerializeCmds(cmds));
                writtenMaps++;
            }

            replayZip.AddEntry("info", ReplayInfo.Write(new ReplayInfo
            {
                name = fileNameNoExtension,
                playerFaction = -1,
                spectatorFaction = -1,
                protocol = MpVersion.Protocol,
                rwVersion = VersionControl.CurrentVersionStringWithRev,
                modIds = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToList(),
                modNames = LoadedModManager.RunningModsListForReading.Select(m => m.Name).ToList(),
                asyncTime = false,
                multifaction = false,
                sections = new() { new ReplaySection(0, TickPatch.Timer) }
            }));

            Log.Message($"Bootstrap-debug: wrote replay {fileNameNoExtension}: maps={writtenMaps}, world bytes={snapshot.GameData?.Length ?? -1}");
        }

        var dstZip = new FileInfo(zipPath);
        if (dstZip.Exists) dstZip.Delete();
        File.Move(tmpZipPath, zipPath);
    }
}
