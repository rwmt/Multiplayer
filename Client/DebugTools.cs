using Harmony;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems))]
    static class DebugToolsListing_Patch
    {
        static void Postfix(Dialog_DebugActionsMenu __instance)
        {
            if (Multiplayer.Client == null) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            var menu = __instance;

            menu.DoLabel("Multiplayer");

            menu.DebugToolMap("T: Destroy thing", DestroyThing);
            menu.DebugAction("Save map", SaveMap);
            menu.DebugAction("Advance time", AdvanceTime);
        }

        [SyncMethod(SyncContext.MapMouseCell)]
        static void DestroyThing()
        {
            foreach (Thing thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
                thing.Destroy(DestroyMode.Vanish);
        }

        [SyncMethod]
        public static void AdvanceTime()
        {
            int to = 148 * 1000;
            if (Find.TickManager.TicksGame < to)
            {
                Find.TickManager.ticksGameInt = to;
                Find.Maps[0].AsyncTime().mapTicks = to;
            }
        }

        [SyncMethod]
        public static void SaveMap()
        {
            Map map = Find.Maps[0];
            byte[] mapData = ScribeUtil.WriteExposable(Current.Game, "map", true);
            File.WriteAllBytes($"map_0_{Multiplayer.username}.xml", mapData);
        }
    }
}
