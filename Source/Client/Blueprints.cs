using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Verse;

namespace Multiplayer.Client
{
    // Allow different factions' blueprints in the same cell
    // Ignore other factions' blueprints when building
    // Remove all blueprints when something solid is built over them
    // Don't draw other factions' blueprints
    // Don't link graphics of different factions' blueprints

    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt))]
    static class CanPlaceBlueprintAtPatch
    {
        static MethodInfo CanPlaceBlueprintOver = AccessTools.Method(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintOver));
        public static MethodInfo ShouldIgnore1Method = AccessTools.Method(typeof(CanPlaceBlueprintAtPatch), nameof(ShouldIgnore), new[] { typeof(Thing) });
        public static MethodInfo ShouldIgnore2Method = AccessTools.Method(typeof(CanPlaceBlueprintAtPatch), nameof(ShouldIgnore), new[] { typeof(ThingDef), typeof(Thing) });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Call && inst.operand == CanPlaceBlueprintOver)
                {
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 22);
                    yield return new CodeInstruction(OpCodes.Call, ShouldIgnore1Method);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }

        static bool ShouldIgnore(ThingDef newThing, Thing oldThing) => newThing.IsBlueprint && ShouldIgnore(oldThing);

        static bool ShouldIgnore(Thing oldThing) => oldThing.def.IsBlueprint && oldThing.Faction != Faction.OfPlayer;
    }

    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt))]
    static class CanPlaceBlueprintAtPatch2
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
        {
            List<CodeInstruction> insts = (List<CodeInstruction>)e;

            int loop1 = new CodeFinder(original, insts).
                Forward(OpCodes.Ldstr, "IdenticalThingExists").
                Backward(OpCodes.Ldarg_S, (byte)5);

            insts.Insert(
                loop1 - 1,
                new CodeInstruction(OpCodes.Ldloc_S, 5),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, insts[loop1 + 2].operand)
            );

            int loop2 = new CodeFinder(original, insts).
                Forward(OpCodes.Ldstr, "InteractionSpotBlocked").
                Backward(OpCodes.Ldarg_S, (byte)5);

            insts.Insert(
                loop2 - 3,
                new CodeInstruction(OpCodes.Ldloc_S, 8),
                new CodeInstruction(OpCodes.Ldloc_S, 9),
                new CodeInstruction(OpCodes.Callvirt, SpawnBuildingAsPossiblePatch.ThingListGet),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, insts[loop2 + 2].operand)
            );

            int loop3 = new CodeFinder(original, insts).
                Forward(OpCodes.Ldstr, "WouldBlockInteractionSpot").
                Backward(OpCodes.Ldarg_S, (byte)5);

            insts.Insert(
                loop3 - 1,
                new CodeInstruction(OpCodes.Ldloc_S, 14),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, insts[loop3 + 2].operand)
            );

            return insts;
        }
    }

    [HarmonyPatch(typeof(PlaceWorker_NeverAdjacentTrap), nameof(PlaceWorker_NeverAdjacentTrap.AllowsPlacing))]
    static class PlaceWorkerTrapPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> e, MethodBase original)
        {
            List<CodeInstruction> insts = (List<CodeInstruction>)e;
            Label label = gen.DefineLabel();

            var finder = new CodeFinder(original, insts);
            int pos = finder.Forward(OpCodes.Stloc_S, 5);

            insts.Insert(
                pos + 1,
                new CodeInstruction(OpCodes.Ldloc_S, 5),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, label)
            );

            int ret = finder.Start().Forward(OpCodes.Ret);
            insts[ret + 1].labels.Add(label);

            return insts;
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.WipeExistingThings))]
    static class WipeExistingThingsPatch
    {
        static MethodInfo SpawningWipes = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Call && inst.operand == SpawningWipes)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldloc_3);
                    yield return new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore2Method);
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.WipeAndRefundExistingThings))]
    static class WipeAndRefundExistingThingsPatch
    {
        static MethodInfo SpawningWipes = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Call && inst.operand == SpawningWipes)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 4);
                    yield return new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore2Method);
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawnBuildingAsPossible))]
    static class SpawnBuildingAsPossiblePatch
    {
        static MethodInfo SpawningWipes = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes));
        public static MethodInfo ThingListGet = AccessTools.Method(typeof(List<Thing>), "get_Item");
        static FieldInfo ThingDefField = AccessTools.Field(typeof(Thing), "def");

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Call && inst.operand == SpawningWipes)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, ThingDefField);
                    yield return new CodeInstruction(OpCodes.Ldloc_3);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 4);
                    yield return new CodeInstruction(OpCodes.Callvirt, ThingListGet);
                    yield return new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore2Method);
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GenPlace), nameof(GenPlace.HaulPlaceBlockerIn))]
    static class HaulPlaceBlockerInPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> e, MethodBase original)
        {
            List<CodeInstruction> insts = (List<CodeInstruction>)e;
            Label label = gen.DefineLabel();

            CodeFinder finder = new CodeFinder(original, insts);
            int pos = finder.Forward(OpCodes.Stloc_2);

            insts.Insert(
                pos + 1,
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, label)
            );

            int ret = finder.End().Advance(-1).Backward(OpCodes.Ret);
            insts[ret + 1].labels.Add(label);

            return insts;
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    static class SpawningWipesBlueprintPatch
    {
        static void Postfix(ref bool __result, BuildableDef newEntDef, BuildableDef oldEntDef)
        {
            ThingDef newDef = newEntDef as ThingDef;
            ThingDef oldDef = oldEntDef as ThingDef;
            if (newDef == null || oldDef == null) return;

            if (!newDef.IsBlueprint && oldDef.IsBlueprint &&
                !GenConstruct.CanPlaceBlueprintOver(GenConstruct.BuiltDefOf(oldDef), newDef))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Print))]
    static class BlueprintPrintPatch
    {
        static bool Prefix(Thing __instance)
        {
            if (Multiplayer.Client == null || !__instance.def.IsBlueprint) return true;
            return __instance.Faction == null || __instance.Faction == Multiplayer.RealPlayerFaction;
        }
    }

    // LinkGrid is one building per cell, so only the player faction's blueprints are shown and linked
    [HarmonyPatch(typeof(LinkGrid), nameof(LinkGrid.Notify_LinkerCreatedOrDestroyed))]
    static class LinkGridBlueprintPatch
    {
        static bool Prefix(Thing linker)
        {
            return !linker.def.IsBlueprint || linker.Faction == Multiplayer.RealPlayerFaction;
        }
    }

    // todo revisit for pvp
    //[HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.DesignateSingleCell))]
    static class DisableInstaBuild
    {
        static MethodInfo GetStatValueAbstract = AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValueAbstract));
        static MethodInfo WorkToBuildMethod = AccessTools.Method(typeof(DisableInstaBuild), nameof(WorkToBuild));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
        {
            List<CodeInstruction> insts = (List<CodeInstruction>)e;
            int pos = new CodeFinder(original, insts).Forward(OpCodes.Call, GetStatValueAbstract);
            insts[pos + 1] = new CodeInstruction(OpCodes.Call, WorkToBuildMethod);

            return insts;
        }

        static float WorkToBuild() => Multiplayer.Client == null ? 0f : -1f;
    }

    [HarmonyPatch(typeof(Frame))]
    [HarmonyPatch(nameof(Frame.WorkToBuild), MethodType.Getter)]
    static class NoZeroWorkFrames
    {
        static void Postfix(ref float __result)
        {
            __result = Math.Max(5, __result); // >=5 otherwise the game complains about jobs starting too fast
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), nameof(WorkGiver_ConstructDeliverResourcesToBlueprints.NoCostFrameMakeJobFor))]
    static class OnlyConstructorsPlaceNoCostFrames
    {
        static MethodInfo IsConstructionMethod = AccessTools.Method(typeof(OnlyConstructorsPlaceNoCostFrames), nameof(IsConstruction));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Isinst && inst.operand == typeof(Blueprint))
                {
                    yield return new CodeInstruction(OpCodes.Ldnull);
                    yield return new CodeInstruction(OpCodes.Cgt_Un);

                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, IsConstructionMethod);

                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }

        static bool IsConstruction(WorkGiver w) => w.def.workType == WorkTypeDefOf.Construction;
    }

}
