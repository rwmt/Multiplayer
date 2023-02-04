using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Saving;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{

    public class MpSettings : ModSettings
    {
        public string username;
        public bool showCursors = true;
        public bool autoAcceptSteam;
        public bool transparentChat = true;
        public int autosaveSlots = 5;
        public bool showDevInfo;
        public int desyncTracesRadius = 40;
        public string serverAddress = "127.0.0.1";
        public bool appendNameToAutosave;
        public bool showModCompatibility = true;
        public bool hideTranslationMods = true;
        public bool enablePings = true;
        public KeyCode? sendPingButton = KeyCode.Mouse4;
        public KeyCode? jumpToPingButton = KeyCode.Mouse3;
        public Rect chatRect;
        public Vector2 resolutionForChat;
        public bool showMainMenuAnim = true;
        public DesyncTracingMode desyncTracingMode = DesyncTracingMode.Fast;
        public bool transparentPlayerCursors = true;
        public List<ColorRGB> playerColors = new(DefaultPlayerColors);
        private (string r, string g, string b)[] colorsBuffer = { };

        private Vector2 scrollPosition = Vector2.zero;
        private SettingsPage currentPage = SettingsPage.General;

        private static readonly ColorRGB[] DefaultPlayerColors =
        {
            new(0,125,255),
            new(255,0,0),
            new(0,255,45),
            new(255,0,150),
            new(80,250,250),
            new(200,255,75),
            new(100,0,75)
        };

        public ServerSettings serverSettings = new();

        public override void ExposeData()
        {
            // Remember to mirror the default values

            Scribe_Values.Look(ref username, "username");
            Scribe_Values.Look(ref showCursors, "showCursors", true);
            Scribe_Values.Look(ref autoAcceptSteam, "autoAcceptSteam");
            Scribe_Values.Look(ref transparentChat, "transparentChat", true);
            Scribe_Values.Look(ref autosaveSlots, "autosaveSlots", 5);
            Scribe_Values.Look(ref showDevInfo, "showDevInfo");
            Scribe_Values.Look(ref desyncTracesRadius, "desyncTracesRadius", 40);
            Scribe_Values.Look(ref serverAddress, "serverAddress", "127.0.0.1");
            Scribe_Values.Look(ref showModCompatibility, "showModCompatibility", true);
            Scribe_Values.Look(ref hideTranslationMods, "hideTranslationMods", true);
            Scribe_Values.Look(ref enablePings, "enablePings", true);
            Scribe_Values.Look(ref sendPingButton, "sendPingButton", KeyCode.Mouse4);
            Scribe_Values.Look(ref jumpToPingButton, "jumpToPingButton", KeyCode.Mouse3);
            Scribe_Custom.LookRect(ref chatRect, "chatRect");
            Scribe_Values.Look(ref resolutionForChat, "resolutionForChat");
            Scribe_Values.Look(ref showMainMenuAnim, "showMainMenuAnim", true);
            Scribe_Values.Look(ref appendNameToAutosave, "appendNameToAutosave");
            Scribe_Values.Look(ref transparentPlayerCursors, "transparentPlayerCursors", true);

            Scribe_Collections.Look(ref playerColors, "playerColors", LookMode.Deep);
            if (playerColors.NullOrEmpty())
                playerColors = new List<ColorRGB>(DefaultPlayerColors);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                PlayerManager.PlayerColors = playerColors.ToArray();

            Scribe_Deep.Look(ref serverSettings, "serverSettings");
            serverSettings ??= new ServerSettings();
        }

        private string slotsBuffer;
        private string desyncRadiusBuffer;

        public void DoSettingsWindowContents(Rect inRect)
        {
            var buttonPos = new Rect(inRect.xMax - 150, inRect.yMin + 10, 125, 32);

            Widgets.Dropdown(buttonPos, currentPage, x => x, GeneratePageMenu, $"MpSettingsPage{currentPage}".Translate());

            switch (currentPage)
            {
                default:
                case SettingsPage.General:
                    DoGeneralSettings(inRect, buttonPos);
                    break;
                case SettingsPage.Color:
                    DoColorContents(inRect, buttonPos);
                    break;
            }
        }

        private IEnumerable<Widgets.DropdownMenuElement<SettingsPage>> GeneratePageMenu(SettingsPage p)
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

        public void DoGeneralSettings(Rect inRect, Rect pageButtonPos)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.ColumnWidth = 270f;

            DoUsernameField(listing);
            listing.TextFieldNumericLabeled("MpAutosaveSlots".Translate() + ":  ", ref autosaveSlots, ref slotsBuffer, 1f, 99f);

            listing.CheckboxLabeled("MpShowPlayerCursors".Translate(), ref showCursors);
            listing.CheckboxLabeled("MpPlayerCursorTransparency".Translate(), ref transparentPlayerCursors);
            listing.CheckboxLabeled("MpAutoAcceptSteam".Translate(), ref autoAcceptSteam, "MpAutoAcceptSteamDesc".Translate());
            listing.CheckboxLabeled("MpTransparentChat".Translate(), ref transparentChat);
            listing.CheckboxLabeled("MpAppendNameToAutosave".Translate(), ref appendNameToAutosave);
            listing.CheckboxLabeled("MpShowModCompat".Translate(), ref showModCompatibility, "MpShowModCompatDesc".Translate());
            listing.CheckboxLabeled("MpEnablePingsSetting".Translate(), ref enablePings);
            listing.CheckboxLabeled("MpShowMainMenuAnimation".Translate(), ref showMainMenuAnim);

            const string buttonOff = "Off";

            using (MpStyle.Set(TextAnchor.MiddleCenter))
                if (listing.ButtonTextLabeled("MpPingLocButtonSetting".Translate(), sendPingButton != null ? $"Mouse {sendPingButton - (int)KeyCode.Mouse0 + 1}" : buttonOff))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>(ButtonChooser(b => sendPingButton = b))));

            using (MpStyle.Set(TextAnchor.MiddleCenter))
                if (listing.ButtonTextLabeled("MpJumpToPingButtonSetting".Translate(), jumpToPingButton != null ? $"Mouse {jumpToPingButton - (int)KeyCode.Mouse0 + 1}" : buttonOff))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>(ButtonChooser(b => jumpToPingButton = b))));

            if (Prefs.DevMode)
            {
                listing.CheckboxLabeled("Show debug info", ref showDevInfo);
                listing.TextFieldNumericLabeled("Desync radius:  ", ref desyncTracesRadius, ref desyncRadiusBuffer, 1f, 200f);

#if DEBUG
                using (MpStyle.Set(TextAnchor.MiddleCenter))
                    if (listing.ButtonTextLabeled("Desync tracing mode", desyncTracingMode.ToString()))
                        desyncTracingMode = desyncTracingMode.Cycle();
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

        private void DoColorContents(Rect inRect, Rect pageButtonPos)
        {
            var viewRect = new Rect(inRect)
            {
                height = (playerColors.Count + 1) * 32f,
                width = inRect.width - 20f,
            };

            var rect = new Rect(pageButtonPos.xMin - 150, pageButtonPos.yMin, 125, 32);
            if (Widgets.ButtonText(rect, "MpResetColors".Translate()))
            {
                playerColors = new List<ColorRGB>(DefaultPlayerColors);
                PlayerManager.PlayerColors = playerColors.ToArray();
            }

            if (playerColors.Count != colorsBuffer.Length)
            {
                colorsBuffer = new (string r, string g, string b)[playerColors.Count];
            }

            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

            var toRemove = -1;
            for (var i = 0; i < playerColors.Count; i++)
            {
                var colors = playerColors[i];
                if (DrawColorRow(i * 32 + 120, ref colors, ref colorsBuffer[i], out var edited))
                    toRemove = i;
                if (edited)
                {
                    playerColors[i] = colors;
                    PlayerManager.PlayerColors = playerColors.ToArray();
                }
            }

            rect = new Rect(402, playerColors.Count * 32 + 118, 32, 32);
            if (Widgets.ButtonText(rect, "+"))
            {
                var rand = new System.Random();
                playerColors.Add(new ColorRGB((byte)rand.Next(256), (byte)rand.Next(256), (byte)rand.Next(256)));
                PlayerManager.PlayerColors = playerColors.ToArray();
            }

            Widgets.EndScrollView();

            if (toRemove >= 0)
            {
                playerColors.RemoveAt(toRemove);
                PlayerManager.PlayerColors = playerColors.ToArray();
            }
        }

        private bool DrawColorRow(int pos, ref ColorRGB color, ref (string r, string g, string b) buffer, out bool edited)
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
                color = new ColorRGB((byte)r, (byte)g, (byte)b);
                edited = true;
            }
            else edited = false;

            if (playerColors.Count > 1)
            {
                rect = new Rect(402, pos - 2, 32, 32);
                return Widgets.ButtonText(rect, "-");
            }
            return false;
        }

        const string UsernameField = "UsernameField";

        private void DoUsernameField(Listing_Standard listing)
        {
            GUI.SetNextControlName(UsernameField);

            var prevField = username;
            var fieldStr = listing.TextEntryLabeled("MpUsernameSetting".Translate() + ":  ", username);

            if (prevField != fieldStr && fieldStr.Length <= 15 && ServerJoiningState.UsernamePattern.IsMatch(fieldStr))
            {
                username = fieldStr;
                Multiplayer.username = fieldStr;
            }

            // Don't allow changing the username while playing
            if (Multiplayer.Client != null && GUI.GetNameOfFocusedControl() == UsernameField)
                UI.UnfocusCurrentControl();
        }

        private enum SettingsPage
        {
            General, Color,
        }
    }

    public enum DesyncTracingMode
    {
        None, Fast, Slow
    }
}
