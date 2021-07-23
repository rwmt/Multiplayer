using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Multiplayer.Common;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class MpSettings : ModSettings
    {
        // Remember to mirror the default values

        public string username;
        public bool showCursors = true;
        public bool autoAcceptSteam;
        public bool transparentChat = true;
        public int autosaveSlots = 5;
        public bool aggressiveTicking = true;
        public bool syncModConfigs = true;
        public bool showDevInfo;
        public int desyncTracesRadius = 40;
        public string serverAddress = "127.0.0.1";
        public bool appendNameToAutosave;
        public bool pauseAutosaveCounter = true;
        public bool showModCompatibility = true;
        public ServerSettings serverSettings = new ServerSettings();
        public Rect chatRect;
        public Vector2 resolutionForChat;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref username, "username");
            Scribe_Values.Look(ref showCursors, "showCursors", true);
            Scribe_Values.Look(ref autoAcceptSteam, "autoAcceptSteam");
            Scribe_Values.Look(ref transparentChat, "transparentChat", true);
            Scribe_Values.Look(ref autosaveSlots, "autosaveSlots", 5);
            Scribe_Values.Look(ref aggressiveTicking, "aggressiveTicking", true);
            Scribe_Values.Look(ref syncModConfigs, "syncModConfigs", true);
            Scribe_Values.Look(ref showDevInfo, "showDevInfo");
            Scribe_Values.Look(ref desyncTracesRadius, "desyncTracesRadius", 40);
            Scribe_Values.Look(ref serverAddress, "serverAddress", "127.0.0.1");
            Scribe_Values.Look(ref pauseAutosaveCounter, "pauseAutosaveCounter", true);
            Scribe_Values.Look(ref showModCompatibility, "showModCompatibility", true);
            ScribeUtil.LookRect(ref chatRect, "chatRect");
            Scribe_Values.Look(ref resolutionForChat, "resolutionForChat");

            Scribe_Deep.Look(ref serverSettings, "serverSettings");

            if (serverSettings == null)
                serverSettings = new ServerSettings();
        }

        private string slotsBuffer;
        private string desyncRadiusBuffer;

        public void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.ColumnWidth = 220f;

            DoUsernameField(listing);
            listing.TextFieldNumericLabeled("MpAutosaveSlots".Translate() + ":  ", ref autosaveSlots, ref slotsBuffer, 1f, 99f);

            listing.CheckboxLabeled("MpShowPlayerCursors".Translate(), ref showCursors);
            listing.CheckboxLabeled("MpAutoAcceptSteam".Translate(), ref autoAcceptSteam, "MpAutoAcceptSteamDesc".Translate());
            listing.CheckboxLabeled("MpTransparentChat".Translate(), ref transparentChat);
            listing.CheckboxLabeled("MpAggressiveTicking".Translate(), ref aggressiveTicking, "MpAggressiveTickingDesc".Translate());
            listing.CheckboxLabeled("MpSyncModConfigs".Translate(), ref syncModConfigs, "MpSyncModConfigsDesc".Translate());

            var appendNameToAutosaveLabel = $"{"MpAppendNameToAutosave".Translate()}:  ";
            listing.CheckboxLabeled(appendNameToAutosaveLabel, ref appendNameToAutosave);

            listing.CheckboxLabeled("MpPauseAutosaveCounter".Translate(), ref pauseAutosaveCounter, "MpPauseAutosaveCounterDesc".Translate());
            listing.CheckboxLabeled("MpShowModCompatibility".Translate(), ref showModCompatibility, "MpShowModCompatibilityDesc".Translate());

            if (Prefs.DevMode)
            {
                listing.CheckboxLabeled("Show debug info", ref showDevInfo);
                listing.TextFieldNumericLabeled("Desync Radius:  ", ref desyncTracesRadius, ref desyncRadiusBuffer, 1f, 200f);
            }

            listing.End();
        }

        const string UsernameField = "UsernameField";

        private void DoUsernameField(Listing_Standard listing)
        {
            GUI.SetNextControlName(UsernameField);

            string username = listing.TextEntryLabeled("MpUsername".Translate() + ":  ", this.username);
            if (username.Length <= 15 && ServerJoiningState.UsernamePattern.IsMatch(username))
            {
                this.username = username;
                Multiplayer.username = username;
            }

            if (Multiplayer.Client != null && GUI.GetNameOfFocusedControl() == UsernameField)
                UI.UnfocusCurrentControl();
        }
    }

}
