using System.IO;
using Multiplayer.Client.Util;
using Multiplayer.Common.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

[HotSwappable]
public class SaveGameWindow : Window
{
    public override Vector2 InitialSize => new(350f, 175f);

    private string curText;
    private bool fileExists;

    public SaveGameWindow(string gameName)
    {
        closeOnClickedOutside = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnAccept = true;
        curText = GenFile.SanitizedFileName(gameName);
    }

    public override void DoWindowContents(Rect inRect)
    {
        GUILayout.BeginArea(inRect.AtZero());
        GUILayout.BeginVertical();

        L.Label("MpSaveGameAs".Translate());

        UpdateText(ref curText, GUILayout.TextField(curText));

        using (MpStyle.Set(GameFont.Tiny))
            L.Label(fileExists ? "MpWillOverwrite".Translate() : "");

        L.BeginHorizCenter();
        {
            if (L.Button("OK".Translate(), 120f))
                Accept(false);

            if (Prefs.DevMode && L.Button("Dev: save replay", 120f))
                Accept(true);
        }
        L.EndHorizCenter();

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
            LongEventHandler.QueueLongEvent(() => MultiplayerSession.SaveGameToFile(curText, currentReplay), "MpSaving", false, null);
            Close();
        }
    }
}
