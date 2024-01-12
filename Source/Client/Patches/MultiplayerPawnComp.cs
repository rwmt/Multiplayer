using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerPawnComp : ThingComp
    {
        public int lastMap = -1;
        public int worldPawnRemoveTick = -1;
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.InitializeComps))]
    static class InitializeCompsPatch
    {
        static void Postfix(ThingWithComps __instance)
        {
            if (__instance is Pawn)
            {
                // Initialize comps if null, otherwise AllComps will return ThingWithComps.EmptyCompsList
                __instance.comps ??= new List<ThingComp>();
                MultiplayerPawnComp comp = new MultiplayerPawnComp() {parent = __instance};
                __instance.AllComps.Add(comp);
            }
        }
    }
}
