using Harmony;
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
    // Remove blueprints when something is built over them
    // Don't draw other factions' blueprints
    // Don't link graphics of different factions' blueprints

    public class CodeFinder
    {
        private int pos;
        private List<CodeInstruction> list;

        public int Pos => pos;

        public CodeFinder(List<CodeInstruction> list)
        {
            this.list = list;
        }

        public CodeFinder Advance(int steps)
        {
            pos += steps;
            return this;
        }

        public CodeFinder Forward(OpCode opcode, object operand = null)
        {
            Find(opcode, operand, 1);
            return this;
        }

        public CodeFinder Backward(OpCode opcode, object operand = null)
        {
            Find(opcode, operand, -1);
            return this;
        }

        public CodeFinder Find(OpCode opcode, object operand, int direction)
        {
            Find(i => Matches(i, opcode, operand), direction);
            return this;
        }

        public CodeFinder Find(Predicate<CodeInstruction> predicate, int direction)
        {
            while (pos < list.Count && pos >= 0)
            {
                if (predicate(list[pos])) return this;
                pos += direction;
            }

            throw new Exception("Couldn't find instruction.");
        }

        public CodeFinder Start()
        {
            pos = 0;
            return this;
        }

        public CodeFinder End()
        {
            pos = list.Count - 1;
            return this;
        }

        private bool Matches(CodeInstruction inst, OpCode opcode, object operand)
        {
            if (inst.opcode != opcode) return false;
            if (operand == null) return true;

            if (opcode == OpCodes.Stloc_S)
                return (inst.operand as LocalBuilder).LocalIndex == (int)operand;

            return Equals(inst.operand, operand);
        }

        public static implicit operator int(CodeFinder finder)
        {
            return finder.pos;
        }
    }

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
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            List<CodeInstruction> insts = (List<CodeInstruction>)e;

            int loop1 = new CodeFinder(insts).
                Forward(OpCodes.Ldstr, "IdenticalThingExists").
                Backward(OpCodes.Ldarg_S, (byte)5);

            insts.Insert(
                loop1 - 1,
                new CodeInstruction(OpCodes.Ldloc_S, 5),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, insts[loop1 + 2].operand)
            );

            int loop2 = new CodeFinder(insts).
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

            int loop3 = new CodeFinder(insts).
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
        static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> e)
        {
            List<CodeInstruction> insts = (List<CodeInstruction>)e;
            Label label = gen.DefineLabel();

            var finder = new CodeFinder(insts);
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
                    yield return new CodeInstruction(OpCodes.Ldloc_2);
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
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 5);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 6);
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
        static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> e)
        {
            List<CodeInstruction> insts = (List<CodeInstruction>)e;
            Label label = gen.DefineLabel();

            CodeFinder finder = new CodeFinder(insts);
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
            if (Multiplayer.client == null || !__instance.def.IsBlueprint) return true;
            return __instance.Faction == null || __instance.Faction == Multiplayer.RealPlayerFaction;
        }
    }

    [HarmonyPatch(typeof(LinkGrid), nameof(LinkGrid.Notify_LinkerCreatedOrDestroyed))]
    static class LinkGridBlueprintPatch
    {
        static bool Prefix(Thing linker)
        {
            return !linker.def.IsBlueprint || linker.Faction == Multiplayer.RealPlayerFaction;
        }
    }

}
