using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class MultiplayerMod : Mod
    {
        public static HarmonyInstance harmony = HarmonyInstance.Create("multiplayer");
        public static MpSettings settings;

        public MultiplayerMod(ModContentPack pack) : base(pack)
        {
            EarlyMarkNoInline();
            EarlyPatches();
            EarlyInit();

            settings = GetSettings<MpSettings>();
        }

        private void EarlyMarkNoInline()
        {
            foreach (var type in MpUtil.AllModTypes())
            {
                MpPatchExtensions.DoMpPatches(null, type)?.ForEach(m => MpUtil.MarkNoInlining(m));

                var harmonyMethods = type.GetHarmonyMethods();
                if (harmonyMethods?.Count > 0)
                {
                    var original = MpUtil.GetOriginalMethod(HarmonyMethod.Merge(harmonyMethods));
                    if (original != null)
                        MpUtil.MarkNoInlining(original);
                }
            }
        }

        private void EarlyPatches()
        {
            {
                var prefix = new HarmonyMethod(AccessTools.Method(typeof(CaptureThingSetMakers), "Prefix"));
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_MarketValue)), prefix);
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_Nutrition)), prefix);
            }
        }

        private void EarlyInit()
        {
            foreach (var thingMaker in DefDatabase<ThingSetMakerDef>.AllDefs)
            {
                CaptureThingSetMakers.captured.Add(thingMaker.root);

                if (thingMaker.root is ThingSetMaker_Sum sum)
                    sum.options.Select(o => o.thingSetMaker).Do(CaptureThingSetMakers.captured.Add);

                if (thingMaker.root is ThingSetMaker_Conditional cond)
                    CaptureThingSetMakers.captured.Add(cond.thingSetMaker);

                if (thingMaker.root is ThingSetMaker_RandomOption rand)
                    rand.options.Select(o => o.thingSetMaker).Do(CaptureThingSetMakers.captured.Add);
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.ColumnWidth = 220f;

            DoUsernameField(listing);

            listing.CheckboxLabeled("MpShowPlayerCursors".Translate(), ref settings.showCursors);
            listing.CheckboxLabeled("MpAutoAcceptSteam".Translate(), ref settings.autoAcceptSteam, "MpAutoAcceptSteamDesc".Translate());

            listing.End();
        }

        private void DoUsernameField(Listing_Standard listing)
        {
            GUI.SetNextControlName("UsernameField");

            string username = listing.TextEntryLabeled($"{"MpUsername".Translate()}:  ", settings.username);
            if (username.Length <= 15 && ServerJoiningState.UsernamePattern.IsMatch(username))
            {
                settings.username = username;
                Multiplayer.username = username;
            }

            if (Multiplayer.Client != null && GUI.GetNameOfFocusedControl() == "UsernameField")
                UI.UnfocusCurrentControl();
        }

        public override string SettingsCategory() => "Multiplayer";
    }

    public class MpSettings : ModSettings
    {
        public string username;
        public bool showCursors = true;
        public bool autoAcceptSteam;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref username, "username");
            Scribe_Values.Look(ref showCursors, "showCursors", true);
            Scribe_Values.Look(ref autoAcceptSteam, "autoAcceptSteam");
        }
    }
}
