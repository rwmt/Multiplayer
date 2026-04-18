using System;
using System.Threading;
using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using Multiplayer.Common.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

public partial class BootstrapConfiguratorWindow
{
    private void DrawSettings(Rect entry, Rect inRect)
    {
        if (!string.IsNullOrEmpty(statusText))
        {
            var statusHeight = Text.CalcHeight(statusText, entry.width);
            Widgets.Label(entry.Height(statusHeight), statusText);
            entry = entry.Down(statusHeight + 4);
        }

        if (isUploadingToml)
        {
            var barRect = entry.Height(20f);
            Widgets.FillableBar(barRect, uploadProgress);
            entry = entry.Down(24f);
        }

        using (MpStyle.Set(TextAnchor.MiddleLeft))
        {
            DoTabButton(entry.Width(140f).Height(40f), Tab.Connecting);
            DoTabButton(entry.Down(50f).Width(140f).Height(40f), Tab.Gameplay);
            if (Prefs.DevMode)
                DoTabButton(entry.Down(100f).Width(140f).Height(40f), Tab.Preview);
        }

        var contentRect = entry.MinX(entry.xMin + 150f);
        var buffers = new ServerSettingsUI.BufferSet
        {
            MaxPlayersBuffer = settingsUiBuffers.MaxPlayersBuffer,
            AutosaveBuffer = settingsUiBuffers.AutosaveBuffer
        };

        if (tab == Tab.Connecting)
            ServerSettingsUI.DrawNetworkingSettings(contentRect, settings, buffers);
        else if (tab == Tab.Gameplay)
            ServerSettingsUI.DrawGameplaySettingsOnly(contentRect, settings, buffers);
        else if (tab == Tab.Preview)
            DrawPreviewTab(contentRect, inRect.height);

        settingsUiBuffers.MaxPlayersBuffer = buffers.MaxPlayersBuffer;
        settingsUiBuffers.AutosaveBuffer = buffers.AutosaveBuffer;

        DrawSettingsButtons(new Rect(0f, inRect.height - 40f, inRect.width, 35f));
    }

    private void DrawPreviewTab(Rect contentRect, float windowHeight)
    {
        RebuildTomlPreview();
        var previewRect = new Rect(contentRect.x, contentRect.y, contentRect.width, windowHeight - contentRect.y - 50f);
        DrawTomlPreview(previewRect);
    }

    private void DoTabButton(Rect rect, Tab tabToDraw)
    {
        Widgets.DrawOptionBackground(rect, tabToDraw == tab);
        if (Widgets.ButtonInvisible(rect, true))
        {
            tab = tabToDraw;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        float x = rect.x + 10f;
        Texture2D icon = null;
        string label;

        if (tabToDraw == Tab.Connecting)
        {
            icon = MultiplayerStatic.OptionsGeneral;
            label = "MpHostTabConnecting".Translate();
        }
        else if (tabToDraw == Tab.Gameplay)
        {
            icon = MultiplayerStatic.OptionsGameplay;
            label = "MpHostTabGameplay".Translate();
        }
        else
        {
            label = "Preview";
        }

        if (icon != null)
        {
            var iconRect = new Rect(x, rect.y + (rect.height - 20f) / 2f, 20f, 20f);
            GUI.DrawTexture(iconRect, icon);
            x += 30f;
        }

        Widgets.Label(new Rect(x, rect.y, rect.width - x, rect.height), label);
    }

    private void DrawSettingsButtons(Rect inRect)
    {
        Rect nextRect;
        if (Prefs.DevMode)
        {
            var copyRect = new Rect(inRect.x, inRect.y, 150f, inRect.height);
            if (Widgets.ButtonText(copyRect, "Copy TOML"))
            {
                RebuildTomlPreview();
                GUIUtility.systemCopyBuffer = tomlPreview;
                Messages.Message("Copied settings.toml to clipboard", MessageTypeDefOf.SilentInput, false);
            }

            nextRect = new Rect(inRect.xMax - 150f, inRect.y, 150f, inRect.height);
        }
        else
        {
            nextRect = new Rect((inRect.width - 150f) / 2f, inRect.y, 150f, inRect.height);
        }

        var nextEnabled = !isUploadingToml && !settingsUploaded;
        var previousColor = GUI.color;
        if (!nextEnabled)
            GUI.color = new Color(1f, 1f, 1f, 0.5f);

        if (Widgets.ButtonText(nextRect, settingsUploaded ? "Uploaded" : "Next") && nextEnabled)
        {
            RebuildTomlPreview();
            StartUploadSettingsToml();
        }

        GUI.color = previousColor;
    }

    private void StartUploadSettingsToml()
    {
        isUploadingToml = true;
        uploadProgress = 0f;
        statusText = "Uploading server settings...";

        new Thread(() =>
        {
            try
            {
                connection.Send(new ClientBootstrapSettingsPacket(settings));

                OnMainThread.Enqueue(() =>
                {
                    isUploadingToml = false;
                    uploadProgress = 1f;
                    settingsUploaded = true;
                    statusText = "Server settings uploaded. Waiting for the server to request save.zip generation.";
                    step = Step.GenerateMap;
                    saveUploadRequestedByServer = false;

                    bootstrapState = bootstrapState with { SettingsMissing = false, SaveMissing = false };
                });
            }
            catch (Exception e)
            {
                Log.Error($"Bootstrap settings upload failed: {e}");
                OnMainThread.Enqueue(() =>
                {
                    isUploadingToml = false;
                    statusText = $"Failed to upload settings: {e.GetType().Name}: {e.Message}";
                });
            }
        }) { IsBackground = true, Name = "MP Bootstrap settings upload" }.Start();
    }

    private void DrawTomlPreview(Rect inRect)
    {
        Widgets.DrawMenuSection(inRect);
        var inner = inRect.ContractedBy(10f);

        Text.Font = GameFont.Small;
        Widgets.Label(inner.TopPartPixels(22f), "settings.toml preview");

        var previewRect = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
        var content = tomlPreview ?? string.Empty;
        var contentHeight = Text.CalcHeight(content, previewRect.width - 16f) + 20f;
        var viewRect = new Rect(0f, 0f, previewRect.width - 16f, Mathf.Max(previewRect.height, contentHeight));

        Widgets.BeginScrollView(previewRect, ref tomlScroll, viewRect);
        Widgets.Label(new Rect(0f, 0f, viewRect.width, viewRect.height), content);
        Widgets.EndScrollView();
    }

    private void RebuildTomlPreview()
    {
        tomlPreview = "# Generated by Multiplayer bootstrap configurator\n\n" + TomlSettingsCommon.Serialize(settings);
    }
}
