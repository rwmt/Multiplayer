using Multiplayer.Client.Util;
using RimWorld;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class SaveGameWindow : Window
{
    public override Vector2 InitialSize => new(400f, 500f);

    private string curText = "";
    private bool fileExists;
    private bool filesRead;
    private SaveFileReader reader;
    private FileInfo selectedFile;
    private static Vector2 saveListScroll;
    private float saveListHeight;

    public SaveGameWindow(string gameName)
    {
        closeOnClickedOutside = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnAccept = true;
        UpdateText(ref curText, GenFile.SanitizedFileName(gameName));
    }

    private void ReloadFiles()
    {
        reader?.WaitTasks(); // Wait for the existing reader to finish

        reader = new SaveFileReader();
        reader.StartReading();
    }

    public override void DoWindowContents(Rect windowRect)
    {
        if (!filesRead)
        {
            ReloadFiles();
            filesRead = true;
        }

        GUILayout.BeginArea(windowRect.AtZero());
        GUILayout.BeginVertical();

        float margin = 10;

        // Draw text and input box at the top of the window
        using (MpStyle.Set(GameFont.Small))
        {
            Vector2 textSize = Text.CalcSize("MpSaveGameAs".Translate());
            GUILayout.Label("MpSaveGameAs".Translate());
        }

        UpdateText(ref curText, GUILayout.TextField(curText));

        using (MpStyle.Set(GameFont.Tiny))
            GUILayout.Label(fileExists ? "MpWillOverwrite".Translate() : "");

        // Draw save buttons at the bottom of the window
        int buttonWidth = 120;
        int buttonHeight = 35;
        int buttonGap = 5;
        int button1x = Prefs.DevMode ?
            (int)(windowRect.width / 2 - buttonWidth - buttonGap / 2) :
            (int)(windowRect.width / 2 - buttonWidth / 2);
        int button2x = (int)(windowRect.width / 2 + buttonGap / 2);
        int buttonY = (int)(windowRect.yMax - 10 - buttonHeight);

        if (Widgets.ButtonText(new Rect(button1x, buttonY, buttonWidth, buttonHeight), "OK".Translate()))
            Accept(false);
        if (Prefs.DevMode && Widgets.ButtonText(new Rect(button2x, buttonY, buttonWidth, buttonHeight), "Dev: save replay"))
            Accept(true);

        // Draw save list
        windowRect.y += 8;
        Rect outerRect = new Rect(margin, windowRect.yMin + 75, windowRect.width - 2 * margin, windowRect.height - buttonHeight - 100);
        Rect scrollRect = new Rect(0, 0, outerRect.width - 16f, saveListHeight);

        Widgets.BeginScrollView(outerRect, ref saveListScroll, scrollRect, true);

        float y = 0;
        DrawSaveList(reader.MpSaves, scrollRect.width, ref y);

        if (Event.current.type == EventType.Layout)
            saveListHeight = y;

        Widgets.EndScrollView();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void DrawSaveList(List<FileInfo> saves, float width, ref float y)
    {
        for (int i = 0; i < saves.Count; i++)
        {
            var file = saves[i];
            var data = reader.GetData(file);
            Rect entryRect = new Rect(0, y, width, 40);

            if (file == selectedFile)
            {
                Widgets.DrawRectFast(entryRect, new Color(1f, 1f, 0.7f, 0.1f));

                var lineColor = new Color(1, 1, 1, 0.3f);
                Widgets.DrawLine(entryRect.min, entryRect.TopRightCorner(), lineColor, 2f);
                Widgets.DrawLine(entryRect.min - new Vector2(-1, -5), entryRect.BottomLeftCorner() - new Vector2(-1, -2), lineColor, 2f);
                Widgets.DrawLine(entryRect.BottomLeftCorner(), entryRect.max, lineColor, 2f);
                Widgets.DrawLine(entryRect.TopRightCorner() - new Vector2(1, -5), entryRect.max - new Vector2(1, -2), lineColor, 2f);
            }
            else if (i % 2 == 0)
            {
                Widgets.DrawAltRect(entryRect);
            }

            using (MpStyle.Set(TextAnchor.MiddleLeft))
                Widgets.Label(entryRect.Right(10), data?.displayName ?? "Loading...");

            using var _ = MpStyle.Set(new Color(0.6f, 0.6f, 0.6f));
            using var __ = MpStyle.Set(GameFont.Tiny);

            var infoText = new Rect(entryRect.xMax - 120, entryRect.yMin + 3, 120, entryRect.height);
            Widgets.Label(infoText, file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));

            if (data != null)
            {
                if (data.gameName != null)
                {
                    Widgets.Label(infoText.Down(16), data.gameName.Truncate(110));
                }
                else
                {
                    GUI.color = data.VersionColor;
                    Widgets.Label(infoText.Down(16), (data.rwVersion ?? "???").Truncate(110));
                }

                if (!data.HasRwVersion)
                {
                    var rect = new Rect(infoText.x - 80, infoText.y + 8f, 80, 24f);
                    GUI.color = Color.red;

                    Widgets.Label(rect, $"({"EItemUpdateStatus_k_EItemUpdateStatusInvalid".Translate()})");
                    TooltipHandler.TipRegion(rect, new TipSignal("SaveIsUnknownFormat".Translate()));
                }
                else if (data.replay && !data.MajorAndMinorVerEqualToCurrent)
                {
                    GUI.color = new Color(0.8f, 0.8f, 0, 0.6f);
                    var outdated = new Rect(infoText.x - 80, infoText.y + 8f, 80, 24f);
                    Widgets.Label(outdated, "MpSaveOutdated".Translate());

                    var text = "MpSaveOutdatedDesc".Translate(data.rwVersion, VersionControl.CurrentVersionString);
                    TooltipHandler.TipRegion(outdated, text);
                }
            }

            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // Check data != null after drawing the button so draw order doesn't depend on data
            // and IMGUI control ids are the same across frames
            if (Widgets.ButtonInvisible(entryRect, false) && data != null)
            {
                if (Event.current.button == 0)
                {
                    UpdateText(ref curText, file.Name[..file.Name.IndexOf('.')]);
                    selectedFile = file;
                }
            }

            y += 40;
        }
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

        // Clear selectedFile on text change
        selectedFile = null;
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
