using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch]
    internal static class PatchVisualEffectsUsingShouldSpawnMotesAt
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

    [HarmonyPatch(typeof(CompFleckEmitterLongTerm), nameof(CompFleckEmitterLongTerm.EmissionTick))]
    static class PatchEmissionTickShouldSpawnMotesAt
    {
        static MethodInfo OriginalMethod = AccessTools.Method(typeof(GenView), nameof(GenView.ShouldSpawnMotesAt),[typeof(IntVec3), typeof(Map), typeof(bool)]);
        static MethodInfo ReplacementMethod = AccessTools.Method(typeof(PatchEmissionTickShouldSpawnMotesAt), nameof(ShouldSpawnMotesAtReplacement));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo changed = AccessTools.Method(typeof(PatchEmissionTickShouldSpawnMotesAt), nameof(ShouldSpawnMotesAtReplacement));

            foreach (var ins in instructions)
            {
                if (ins.Calls(OriginalMethod))
                    yield return new CodeInstruction(OpCodes.Call, changed);
                else
                    yield return ins;
            }
        }

        public static bool ShouldSpawnMotesAtReplacement(IntVec3 cell, Map map, bool flag)
        {
            if (Multiplayer.Client == null)
                return cell.ShouldSpawnMotesAt(map, flag);

            bool shouldSpawn;
            sbyte prevMapIndex = Current.Game.currentMapIndex;
            Current.Game.currentMapIndex = (sbyte)map.Index;

            try
            {
                shouldSpawn = cell.ShouldSpawnMotesAt(map, flag);
            }
            finally
            {
                Current.Game.currentMapIndex = prevMapIndex;
            }

            return shouldSpawn;
        }
    }
}
