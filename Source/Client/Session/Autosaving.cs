using System;
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
    {
        Log.Message($"Multiplayer: saving to file {fileNameNoExtension}");

        try
        {
            var tmpPath = Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.tmp.zip");
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);

            Replay.ForSaving(new FileInfo(tmpPath)).WriteData(
                snapshot ?? SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveGameData(), false)
            );

            var dstPath = Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.zip");
            if (File.Exists(dstPath))
                File.Replace(tmpPath, dstPath, destinationBackupFileName: null);
            else
                File.Move(tmpPath, dstPath);

            Messages.Message("MpGameSaved".Translate(fileNameNoExtension), MessageTypeDefOf.SilentInput, false);
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
}
