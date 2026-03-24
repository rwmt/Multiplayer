using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(AreaSource))]
    static class AreaSource_Patch
    {
        [HarmonyPatch(nameof(AreaSource.ComputeAll))]
        [HarmonyPatch(nameof(AreaSource.UpdateIncrementally))]
        static void Prefix(AreaSource __instance, ref AreaManager __state)
        {
            if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction) return;
            __state = __instance.map.areaManager;
            __instance.map.areaManager = __instance.map.MpComp().AllAreaManager();
        }

        [HarmonyPatch(nameof(AreaSource.ComputeAll))]
        [HarmonyPatch(nameof(AreaSource.UpdateIncrementally))]
        static void Finalizer(AreaSource __instance, AreaManager __state)
        {
            if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction) return;

            // restore original
            __instance.map.areaManager = __state;
        }
    }
}
