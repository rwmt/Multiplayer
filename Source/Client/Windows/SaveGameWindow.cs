using System;
using System.IO;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;


public class SaveGameWindow : AbstractTextInputWindow
{
    private bool fileExists;

    public SaveGameWindow(string gameName)
    {
        title = "MpSaveGameAs".Translate();
        curText = GenFile.SanitizedFileName(gameName);
    }

    public override bool Accept()
    {
        if (curText.Length == 0) return false;

        try
        {
            LongEventHandler.QueueLongEvent(() => MultiplayerSession.SaveGameToFile(curText), "MpSaving", false, null);
            Close();
        }
        catch (Exception e)
        {
            Log.Error($"Exception saving replay {e}");
        }

        return true;
    }

    public override bool Validate(string str)
    {
        if (str.Length == 0)
            return true;

        if (str.Length > 30)
            return false;

        if (GenFile.SanitizedFileName(str) != str)
            return false;

        fileExists = new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{str}.zip")).Exists;
        return true;
    }

    public override void DrawExtra(Rect inRect)
    {
        if (fileExists)
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, 25 + 15 + 35, inRect.width, 35f), "MpWillOverwrite".Translate());
            Text.Font = GameFont.Small;
        }
    }
}
