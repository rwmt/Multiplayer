using System;
using System.IO;
using System.Linq;
using Multiplayer.Common;
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
            new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.zip")).Delete();
            Replay.ForSaving(fileNameNoExtension).WriteData(
                currentReplay ?
                    Multiplayer.session.dataSnapshot :
                    SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveGameData(), false)
            );
            Messages.Message("MpGameSaved".Translate(fileNameNoExtension), MessageTypeDefOf.SilentInput, false);
            Multiplayer.session.lastSaveAt = Time.realtimeSinceStartup;
        }
        catch (Exception e)
        {
            Log.Error($"Exception saving multiplayer game as {fileNameNoExtension}: {e}");
            Messages.Message("MpGameSaveFailed".Translate(), MessageTypeDefOf.SilentInput, false);
        }
    }
}
