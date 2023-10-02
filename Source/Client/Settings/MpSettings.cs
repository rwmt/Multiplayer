using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Saving;
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
        public List<ColorRGBClient> playerColors = new(DefaultPlayerColors);

        internal static readonly ColorRGBClient[] DefaultPlayerColors =
        {
            new(0,125,255),
            new(255,0,0),
            new(0,255,45),
            new(255,0,150),
            new(80,250,250),
            new(200,255,75),
            new(100,0,75)
        };

        private ServerSettingsClient serverSettingsClient = new();
        public ServerSettings PreferredLocalServerSettings => serverSettingsClient.settings;

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
                playerColors = new List<ColorRGBClient>(DefaultPlayerColors);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                PlayerManager.PlayerColors = playerColors.Select(c => (ColorRGB)c).ToArray();

            Scribe_Deep.Look(ref serverSettingsClient, "serverSettings");
            serverSettingsClient ??= new ServerSettingsClient();
        }
    }

    public enum DesyncTracingMode
    {
        None, Fast, Slow
    }

    public struct ColorRGBClient : IExposable
    {
        public byte r, g, b;

        public ColorRGBClient(byte r, byte g, byte b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        public void ExposeData()
        {
            ScribeAsInt(ref r, "r");
            ScribeAsInt(ref g, "g");
            ScribeAsInt(ref b, "b");
        }

        private void ScribeAsInt(ref byte value, string label)
        {
            int temp = value;
            Scribe_Values.Look(ref temp, label);
            value = (byte)temp;
        }

        public static implicit operator Color(ColorRGBClient value) => new(value.r / 255f, value.g / 255f, value.b / 255f);

        public static implicit operator ColorRGB(ColorRGBClient value) => new(value.r, value.g, value.b);
    }
}
