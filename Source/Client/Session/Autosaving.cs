using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class Autosaving
{
    // The local save isn't sent to the server: it's taken without the reload every client does at a join point
    // (and in this client's faction context), so a joiner loading it wouldn't match the live clients.
    public static void DoAutosave(bool requestJoinPoint = true)
    {
        // Taken now: in-game day autosaves start at the same tick on every client, but more ticks can run in this
        // frame before the long event saves. The server starts one join point per tick.
        var tick = TickPatch.Timer;

        LongEventHandler.QueueLongEvent(() =>
        {
            var snapshot = SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveGameData(), false);

            if (!SaveGameToFile_Overwrite(GetNextAutosaveFileName(), snapshot))
                return;

            if (requestJoinPoint)
                SendAutosavingRequest(JoinPointRequestReason.Save, tick);
        }, "MpSaving", false, null);
    }

    public static void SendAutosavingRequest(JoinPointRequestReason reason, int tick)
    {
        if (TickPatch.Simulating)
            return;

        Multiplayer.Client.Send(new ClientAutosavingPacket(reason, tick));
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

    public static bool SaveGameToFile_Overwrite(string fileNameNoExtension, bool currentReplay)
        => SaveGameToFile_Overwrite(fileNameNoExtension,
            currentReplay ? Multiplayer.session.dataSnapshot : null);

    public static bool SaveGameToFile_Overwrite(string fileNameNoExtension, GameDataSnapshot snapshot)
        => SaveGameToFile_Overwrite(fileNameNoExtension, file =>
            Replay.ForSaving(file).WriteData(
                snapshot ?? SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveGameData(), false)
            ));

    // writeReplay writes the save to the given (temporary) file
    private static bool SaveGameToFile_Overwrite(string fileNameNoExtension, Action<FileInfo> writeReplay)
    {
        Log.Message($"Multiplayer: saving to file {fileNameNoExtension}");

        try
        {
            var tmpPath = Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.tmp.zip");
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);

            writeReplay(new FileInfo(tmpPath));

            var dstPath = Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.zip");
            if (File.Exists(dstPath))
                File.Replace(tmpPath, dstPath, destinationBackupFileName: null);
            else
                File.Move(tmpPath, dstPath);

            Messages.Message("MpGameSaved".Translate(fileNameNoExtension), MessageTypeDefOf.SilentInput, false);
            if (Multiplayer.session != null)
                Multiplayer.session.lastSaveAt = Time.realtimeSinceStartup;
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Exception saving multiplayer game as {fileNameNoExtension}: {e}");
            Messages.Message("MpGameSaveFailed".Translate(), MessageTypeDefOf.SilentInput, false);
            return false;
        }
    }

    // Called while disconnecting, before the session and the multiplayer game are gone (#333, #937). Keeps what a
    // save needs: the last join point snapshot, which every command received since is appended to, and the replay
    // info. Loading the save simulates the commands up to the tick this client had reached. Returns null when
    // there's no game to save.
    public static Action CaptureDisconnectSave()
    {
        var session = Multiplayer.session;
        if (session == null || session.replay || session.dataSnapshot == null || Multiplayer.arbiterInstance ||
            Multiplayer.Client?.State != ConnectionStateEnum.ClientPlaying ||
            Current.ProgramState != ProgramState.Playing)
            return null;

        try
        {
            var snapshot = session.dataSnapshot with { Cmds = new List<ScheduledCommand>(session.dataSnapshot.Cmds) };
            var endTick = TickPatch.Timer;
            var info = Replay.CurrentInfo();

            var baseName = $"Disconnected-{GenFile.SanitizedFileName(session.gameName ?? "")}";
            var fileName = baseName;
            for (int i = 2; File.Exists(Path.Combine(Multiplayer.ReplaysDir, $"{fileName}.zip")); i++)
                fileName = $"{baseName}-{i}";

            return () => LongEventHandler.QueueLongEvent(() =>
            {
                SaveGameToFile_Overwrite(fileName, file =>
                {
                    info.sections.Clear();
                    Replay.ForSaving(file, info).WriteData(snapshot, endTick);
                });
            }, "MpSaving", false, null);
        }
        catch (Exception e)
        {
            Log.Error($"Multiplayer: couldn't keep the game for saving after disconnecting: {e}");
            return null;
        }
    }
}
