using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(FishShadowComponent), nameof(FishShadowComponent.SpawnFishFleck))]
    internal static class Patch_FishShadowComponent_MapComponentTick
    {
        static void Prefix(Map map, ref sbyte __state)
        {
            if (Multiplayer.Client == null) return;

            __state = Current.Game.currentMapIndex;
            Current.Game.currentMapIndex = (sbyte)map.Index;
        }

        static void Finalizer(sbyte __state)
        {
            if (Multiplayer.Client == null) return;

            Current.Game.currentMapIndex = __state;
        }
    }
}
