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
                MultiplayerPawnComp comp = new MultiplayerPawnComp() {parent = __instance};
                __instance.AllComps.Add(comp);
            }
        }
    }
}
