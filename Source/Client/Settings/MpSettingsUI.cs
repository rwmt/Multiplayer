using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class MpSettingsUI
{
    private static string slotsBuffer;
    private static string desyncRadiusBuffer;

    private static Vector2 scrollPosition = Vector2.zero;
    private static SettingsPage currentPage = SettingsPage.General;

    internal static void DoSettingsWindowContents(MpSettings settings, Rect inRect)
    {
        var buttonPos = new Rect(inRect.xMax - 150, inRect.yMin + 10, 125, 32);

        Widgets.Dropdown(buttonPos, currentPage, x => x, GeneratePageMenu, $"MpSettingsPage{currentPage}".Translate());

        switch (currentPage)
        {
            default:
            case SettingsPage.General:
                DoGeneralSettings(settings, inRect, buttonPos);
                break;
            case SettingsPage.Color:
                DoColorContents(settings, inRect, buttonPos);
                break;
        }
    }

    private static IEnumerable<Widgets.DropdownMenuElement<SettingsPage>> GeneratePageMenu(SettingsPage p)
    {
        return from SettingsPage page in Enum.GetValues(typeof(SettingsPage))
            where page != p
            select new Widgets.DropdownMenuElement<SettingsPage>
            {
                option = new FloatMenuOption($"MpSettingsPage{page}".Translate(), () =>
                {
                    currentPage = page;
                    scrollPosition = Vector2.zero;
                })
                {
                    tooltip = page == SettingsPage.Color ? "MpSettingsPageColorDesc".Translate() : null
                },
                payload = page,
            };
    }

    public static void DoGeneralSettings(MpSettings settings, Rect inRect, Rect pageButtonPos)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.ColumnWidth = 270f;

        DoUsernameField(settings, listing);
        listing.TextFieldNumericLabeled("MpAutosaveSlots".Translate() + ":  ", ref settings.autosaveSlots, ref slotsBuffer, 1f,
            99f);

        listing.CheckboxLabeled("MpShowPlayerCursors".Translate(), ref settings.showCursors);
        listing.CheckboxLabeled("MpPlayerCursorTransparency".Translate(), ref settings.transparentPlayerCursors);
        listing.CheckboxLabeled("MpAutoAcceptSteam".Translate(), ref settings.autoAcceptSteam,
            "MpAutoAcceptSteamDesc".Translate());
        listing.CheckboxLabeled("MpTransparentChat".Translate(), ref settings.transparentChat);
        listing.CheckboxLabeled("MpAppendNameToAutosave".Translate(), ref settings.appendNameToAutosave);
        listing.CheckboxLabeled("MpShowModCompat".Translate(), ref settings.showModCompatibility,
            "MpShowModCompatDesc".Translate());
        listing.CheckboxLabeled("MpEnablePingsSetting".Translate(), ref settings.enablePings);
        listing.CheckboxLabeled("MpShowMainMenuAnimation".Translate(), ref settings.showMainMenuAnim);

        const string buttonOff = "Off";

        using (MpStyle.Set(TextAnchor.MiddleCenter))
            if (listing.ButtonTextLabeled("MpPingLocButtonSetting".Translate(),
                    settings.sendPingButton != null ? $"Mouse {settings.sendPingButton - (int)KeyCode.Mouse0 + 1}" : buttonOff))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>(ButtonChooser(b => settings.sendPingButton = b))));

        using (MpStyle.Set(TextAnchor.MiddleCenter))
            if (listing.ButtonTextLabeled("MpJumpToPingButtonSetting".Translate(),
                    settings.jumpToPingButton != null ? $"Mouse {settings.jumpToPingButton - (int)KeyCode.Mouse0 + 1}" : buttonOff))
                Find.WindowStack.Add(
                    new FloatMenu(new List<FloatMenuOption>(ButtonChooser(b => settings.jumpToPingButton = b))));

        if (Prefs.DevMode)
        {
            listing.CheckboxLabeled("Show debug info", ref settings.showDevInfo);
            listing.TextFieldNumericLabeled("Desync radius:  ", ref settings.desyncTracesRadius, ref desyncRadiusBuffer, 1f,
                200f);

#if DEBUG
            using (MpStyle.Set(TextAnchor.MiddleCenter))
                if (listing.ButtonTextLabeled("Desync tracing mode", settings.desyncTracingMode.ToString()))
                    settings.desyncTracingMode = settings.desyncTracingMode.Cycle();
#endif
        }

        listing.End();

        IEnumerable<FloatMenuOption> ButtonChooser(Action<KeyCode?> setter)
        {
            yield return new FloatMenuOption(buttonOff, () => { setter(null); });

            for (var btn = 0; btn < 5; btn++)
            {
                var b = btn;
                yield return new FloatMenuOption($"Mouse {b + 3}", () => { setter(KeyCode.Mouse2 + b); });
            }
        }
    }

    private static (string r, string g, string b)[] colorsBuffer = { };

    private static void DoColorContents(MpSettings settings, Rect inRect, Rect pageButtonPos)
    {
        var viewRect = new Rect(inRect)
        {
            height = (settings.playerColors.Count + 1) * 32f,
            width = inRect.width - 20f,
        };

        var rect = new Rect(pageButtonPos.xMin - 150, pageButtonPos.yMin, 125, 32);
        if (Widgets.ButtonText(rect, "MpResetColors".Translate()))
        {
            settings.playerColors = new List<ColorRGBClient>(MpSettings.DefaultPlayerColors);
            PlayerManager.PlayerColors = settings.playerColors.Select(c => (ColorRGB)c).ToArray();
        }

        if (settings.playerColors.Count != colorsBuffer.Length)
        {
            colorsBuffer = new (string r, string g, string b)[settings.playerColors.Count];
        }

        Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

        var toRemove = -1;
        for (var i = 0; i < settings.playerColors.Count; i++)
        {
            var colors = settings.playerColors[i];
            if (DrawColorRow(settings, i * 32 + 120, ref colors, ref colorsBuffer[i], out var edited))
                toRemove = i;
            if (edited)
            {
                settings.playerColors[i] = colors;
                PlayerManager.PlayerColors = settings.playerColors.Select(c => (ColorRGB)c).ToArray();
            }
        }

        rect = new Rect(402, settings.playerColors.Count * 32 + 118, 32, 32);
        if (Widgets.ButtonText(rect, "+"))
        {
            var rand = new System.Random();
            settings.playerColors.Add(new ColorRGBClient((byte)rand.Next(256), (byte)rand.Next(256), (byte)rand.Next(256)));
            PlayerManager.PlayerColors = settings.playerColors.Select(c => (ColorRGB)c).ToArray();
        }

        Widgets.EndScrollView();

        if (toRemove >= 0)
        {
            settings.playerColors.RemoveAt(toRemove);
            PlayerManager.PlayerColors = settings.playerColors.Select(c => (ColorRGB)c).ToArray();
        }
    }

    private static bool DrawColorRow(MpSettings settings, int pos, ref ColorRGBClient color, ref (string r, string g, string b) buffer, out bool edited)
    {
        var (r, g, b) = ((int)color.r, (int)color.g, (int)color.b);
        var rect = new Rect(10, pos, 100, 28);
        Widgets.TextFieldNumericLabeled(rect, "R", ref r, ref buffer.r, 0, 255);
        rect = new Rect(120, pos, 100, 28);
        Widgets.TextFieldNumericLabeled(rect, "G", ref g, ref buffer.g, 0, 255);
        rect = new Rect(230, pos, 100, 28);
        Widgets.TextFieldNumericLabeled(rect, "B", ref b, ref buffer.b, 0, 255);

        rect = new Rect(350, pos - 2, 32, 32);
        Widgets.DrawBoxSolid(rect, color);

        if (color.r != r || color.g != g || color.b != b)
        {
            color = new ColorRGBClient((byte)r, (byte)g, (byte)b);
            edited = true;
        }
        else edited = false;

        if (settings.playerColors.Count > 1)
        {
            rect = new Rect(402, pos - 2, 32, 32);
            return Widgets.ButtonText(rect, "-");
        }

        return false;
    }

    const string UsernameField = "UsernameField";

    private static void DoUsernameField(MpSettings settings, Listing_Standard listing)
    {
        GUI.SetNextControlName(UsernameField);

        var prevField = settings.username;
        var fieldStr = listing.TextEntryLabeled("MpUsernameSetting".Translate() + ":  ", settings.username);

        if (prevField != fieldStr && fieldStr.Length <= 15 && MultiplayerServer.UsernamePattern.IsMatch(fieldStr))
        {
            settings.username = fieldStr;
            Multiplayer.username = fieldStr;
        }

        // Don't allow changing the username while playing
        if (Multiplayer.Client != null && GUI.GetNameOfFocusedControl() == UsernameField)
            UI.UnfocusCurrentControl();
    }

    private enum SettingsPage
    {
        General,
        Color,
    }
}
