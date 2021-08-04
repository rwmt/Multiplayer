using HarmonyLib;
using Multiplayer.Common;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client.Patches
{
    public static class DebugPatches
    {
        public static void Init()
        {
            /*harmony.Patch(
                AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)),
                new HarmonyMethod(typeof(MultiplayerMod), nameof(Prefixfactionman))
            );

            harmony.Patch(
                AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.IsPlayer)),
                new HarmonyMethod(typeof(MultiplayerMod), nameof(Prefixfactionman))
            );*/
        }

        static void Prefixfactionman()
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                string trace = new StackTrace().ToString();
                if (!trace.Contains("SetInitialPsyfocusLevel") &&
                    !trace.Contains("Pawn_NeedsTracker.ShouldHaveNeed") &&
                    !trace.Contains("FactionManager.ExposeData"))
                    Log.Message($"factionman call {trace}", true);
            }
        }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    static class SetupQuickTestPatch
    {
        public static bool marker;

        static void Prefix() => marker = true;

        static void Postfix()
        {
            if (MpVersion.IsDebug)
                Find.GameInitData.mapSize = 250;
            marker = false;
        }
    }

    [HarmonyPatch(typeof(GameInitData), nameof(GameInitData.ChooseRandomStartingTile))]
    static class RandomStartingTilePatch
    {
        static void Postfix()
        {
            if (MpVersion.IsDebug && SetupQuickTestPatch.marker)
            {
                Find.GameInitData.startingTile = 501;
                Find.WorldGrid[Find.GameInitData.startingTile].hilliness = Hilliness.SmallHills;
            }
        }
    }

    [HarmonyPatch(typeof(GenText), nameof(GenText.RandomSeedString))]
    static class GrammarRandomStringPatch
    {
        static void Postfix(ref string __result)
        {
            if (MpVersion.IsDebug && SetupQuickTestPatch.marker)
                __result = "multiplayer1";
        }
    }
}
