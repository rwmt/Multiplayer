using System.IO;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class SaveGameWindow : Window
{
    public override Vector2 InitialSize => new(350f, 175f);

    private string curText = "";
    private bool fileExists;

    public SaveGameWindow(string gameName)
    {
        closeOnClickedOutside = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnAccept = true;
        UpdateText(ref curText, GenFile.SanitizedFileName(gameName));
    }

    public override void DoWindowContents(Rect inRect)
    {
        GUILayout.BeginArea(inRect.AtZero());
        GUILayout.BeginVertical();

        MpLayout.Label("MpSaveGameAs".Translate());

        UpdateText(ref curText, GUILayout.TextField(curText));

        using (MpStyle.Set(GameFont.Tiny))
            MpLayout.Label(fileExists ? "MpWillOverwrite".Translate() : "");

        MpLayout.BeginHorizCenter();
        {
            if (MpLayout.Button("OK".Translate(), 120f))
                Accept(false);

            if (Prefs.DevMode && MpLayout.Button("Dev: save replay", 120f))
                Accept(true);
        }
        MpLayout.EndHorizCenter();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    public override void OnAcceptKeyPressed()
    {
        Accept(false);
    }

    private void UpdateText(ref string value, string newValue)
    {
        if (newValue == value)
            return;

        if (newValue.Length > 30)
            return;

        if (!newValue.NullOrEmpty() && GenFile.SanitizedFileName(newValue) != newValue)
            return;

        fileExists = new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{newValue}.zip")).Exists;
        value = newValue;
    }

    private void Accept(bool currentReplay)
    {
        if (curText.Length != 0)
        {
            LongEventHandler.QueueLongEvent(() => Autosaving.SaveGameToFile_Overwrite(curText, currentReplay), "MpSaving", false, null);
            Close();
        }
    }
}
