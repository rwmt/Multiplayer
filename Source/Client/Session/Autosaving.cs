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
    public static void DoAutosave()
    {
        LongEventHandler.QueueLongEvent(() =>
        {
            if (!SaveGameToFile_Overwrite(GetNextAutosaveFileName(), false))
                return;

            Multiplayer.Client.Send(new ClientAutosavingPacket(JoinPointRequestReason.Save));
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

    public static bool SaveGameToFile_Overwrite(string fileNameNoExtension, bool currentReplay)
    {
        Log.Message($"Multiplayer: saving to file {fileNameNoExtension}");

        try
        {
            var tmpPath = Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.tmp.zip");
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);

            Replay.ForSaving(new FileInfo(tmpPath)).WriteData(
                currentReplay ?
                    Multiplayer.session.dataSnapshot :
                    SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveGameData(), false)
            );

            var dstPath = Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.zip");
            if (File.Exists(dstPath))
                File.Delete(dstPath);

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
