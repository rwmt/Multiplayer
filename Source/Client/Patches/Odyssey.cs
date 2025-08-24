using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(FishShadowComponent), nameof(FishShadowComponent.MapComponentTick))]
    public static class FishShadowComponent_MapComponentTick_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo objectHash = AccessTools.Method(typeof(object), nameof(object.GetHashCode));
            MethodInfo waterBodyHash = AccessTools.Method(typeof(FishShadowComponent_MapComponentTick_Patch), nameof(FishShadowComponent_MapComponentTick_Patch.WaterBodyHash));

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(objectHash))
                    yield return new CodeInstruction(OpCodes.Call, waterBodyHash);
                else
                    yield return instruction;
            }
        }

        public static int WaterBodyHash(WaterBody body)
        {
            if (Multiplayer.Client == null)
                return body.GetHashCode();

            return Gen.HashCombineInt(body.map?.uniqueID ?? 0, body.rootCell.x, body.rootCell.z, (int)body.waterBodyType);
        }
    }


    [HarmonyPatch(typeof(CompStatue), nameof(CompStatue.InitFakePawn))]
    public static class PatchInitFakePawnToNotSyncPawnName
    {
        static void Prefix(ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            if(!Multiplayer.dontSync)
            {
                Multiplayer.dontSync = true;
                __state = true;
            }
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Multiplayer.dontSync = false;
        }
    }
}
