using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch]
    internal static class Patch_FishShadowComponent_MapComponentTick
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FishShadowComponent), nameof(FishShadowComponent.SpawnFishFleck));
            yield return AccessTools.Method(typeof(LavaFXComponent), nameof(LavaFXComponent.ThrowLavaSmoke));
        }

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
