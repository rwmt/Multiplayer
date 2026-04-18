using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Util;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Random = UnityEngine.Random;

namespace Multiplayer.Client.Patches
{
    [EarlyPatch]
    [HarmonyPatch(typeof(PawnTweener))]
    [HarmonyPatch(nameof(PawnTweener.TweenedPos), MethodType.Getter)]
    static class DrawPosPatch
    {
        public static bool returnTruePosition = false;

        static bool Prefix() => Multiplayer.Client == null || Multiplayer.InInterface || returnTruePosition;

        // Give the root position during ticking
        static void Postfix(PawnTweener __instance, ref Vector3 __result)
        {
            if (Multiplayer.Client == null || Multiplayer.InInterface || returnTruePosition) return;
            __result = __instance.TweenedPosRoot();
        }
    }

    [HarmonyPatch]
    static class FixApparelSort
    {
        static MethodBase TargetMethod() =>
            MpMethodUtil.GetLambda(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.SortWornApparelIntoDrawOrder));

        static void Postfix(Apparel a, Apparel b, ref int __result)
        {
            if (__result == 0)
                __result = a.thingIDNumber.CompareTo(b.thingIDNumber);
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryGetMeleeVerb))]
    static class TryGetMeleeVerbPatch
    {
        static bool Cancel => Multiplayer.Client != null && Multiplayer.InInterface;

        static bool Prefix()
        {
            // Namely FloatMenuUtility.GetMeleeAttackAction
            return !Cancel;
        }

        static void Postfix(Pawn_MeleeVerbs __instance, Thing target, ref Verb __result)
        {
            if (Cancel)
                __result = __instance.GetUpdatedAvailableVerbsList(false).FirstOrDefault(ve => ve.GetSelectionWeight(target) != 0).verb;
        }
    }

    [HarmonyPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.TryGetRandomUnusedSolidName))]
    static class GenerateNewPawnInternalPatch
    {
        static MethodBase FirstOrDefault = SymbolExtensions.GetMethodInfo<IEnumerable<NameTriple>>(
            e => e.FirstOrDefault()
        );

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            List<CodeInstruction> insts = new List<CodeInstruction>(e);

            for (int i = insts.Count - 1; i >= 0; i--)
            {
                if (insts[i].operand as MethodBase == FirstOrDefault)
                    insts.Insert(
                       i + 1,
                       new CodeInstruction(OpCodes.Ldloc_1),
                       new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GenerateNewPawnInternalPatch), nameof(Unshuffle)).MakeGenericMethod(typeof(NameTriple)))
                   );
            }

            return insts;
        }

        public static void Unshuffle<T>(List<T> list)
        {
            uint iters = Rand.iterations;

            int i = 0;
            while (i < list.Count)
            {
                int index = Mathf.Abs(MurmurHash.GetInt(Rand.seed, iters--) % (i + 1));
                (list[index], list[i]) = (list[i], list[index]);
                i++;
            }
        }
    }

    [HarmonyPatch(typeof(WorldObjectSelectionUtility), nameof(WorldObjectSelectionUtility.VisibleToCameraNow))]
    static class CaravanVisibleToCameraPatch
    {
        static void Postfix(ref bool __result)
        {
            if (!Multiplayer.InInterface)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
    static class WealthWatcherRecalc
    {
        static bool Prefix() => Multiplayer.Client == null || !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(FloodFillerFog), nameof(FloodFillerFog.FloodUnfog))]
    static class FloodUnfogPatch
    {
        static void Postfix(ref FloodUnfogResult __result)
        {
            if (Multiplayer.Client != null)
                __result.allOnScreen = false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ProcessPostTickVisuals))]
    static class DrawTrackerTickPatch
    {
        static MethodInfo CellRectContains = AccessTools.Method(typeof(CellRect), nameof(CellRect.Contains));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand as MethodInfo == CellRectContains)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }
    }

    public static class CellsShufflePatchShared
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, FieldInfo cellsShuffledField)
        {
            bool found = false;
            foreach (CodeInstruction inst in insts)
            {
                yield return inst;
                if (!found && inst.operand as FieldInfo == cellsShuffledField)
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CellsShufflePatchShared), nameof(ShouldShuffle)));
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.Or);
                    found = true;
                }
            }
        }

        public static bool ShouldShuffle()
        {
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }
    }

    [HarmonyPatch(typeof(Zone), nameof(Zone.Cells), MethodType.Getter)]
    static class ZoneCellsShufflePatch
    {
        static readonly FieldInfo CellsShuffled = AccessTools.Field(typeof(Zone), "cellsShuffled");
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
            => CellsShufflePatchShared.Transpiler(insts, CellsShuffled);
    }

    [HarmonyPatch(typeof(Plan), nameof(Plan.Cells), MethodType.Getter)]
    static class PlanCellsShufflePatch
    {
        static readonly FieldInfo CellsShuffled = AccessTools.Field(typeof(Plan), "cellsShuffled");
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
            => CellsShufflePatchShared.Transpiler(insts, CellsShuffled);
    }

    [HarmonyPatch]
    static class SortArchivablesById
    {
        static MethodBase TargetMethod()
        {
            return MpMethodUtil.GetLambda(typeof(Archive), nameof(Archive.Add));
        }

        static void Postfix(IArchivable x, ref int __result)
        {
            if (x is ArchivedDialog dialog)
                __result = dialog.ID;
            else if (x is Letter letter)
                __result = letter.ID;
            else if (x is Message msg)
                __result = msg.ID;
        }
    }

    [HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    static class DangerRatingPatch
    {
        static bool Prefix() => !Multiplayer.InInterface;

        static void Postfix(DangerWatcher __instance, ref StoryDanger __result)
        {
            if (Multiplayer.InInterface)
                __result = __instance.dangerRatingInt;
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.ImmobilizedByMass), MethodType.Getter)]
    static class ImmobilizedByMass_Patch
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(StoryWatcher_PopAdaptation), nameof(StoryWatcher_PopAdaptation.Notify_PawnEvent))]
    static class CancelStoryWatcherEventInInterface
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(AutoSlaughterManager), nameof(AutoSlaughterManager.Notify_ConfigChanged))]
    static class CancelAutoslaughterDirtying
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.AllAbilitiesForReading), MethodType.Getter)]
    static class DontRecacheAbilitiesInInterface
    {
        static bool Prefix() => !Multiplayer.InInterface;

        static void Postfix(Pawn_AbilityTracker __instance, ref List<Ability> __result)
        {
            // The result can be null only if the method gets cancelled by the prefix
            if (__result == null)
                __result = __instance.allAbilitiesCached;
        }
    }

    [HarmonyPatch(typeof(PriorityWork), nameof(PriorityWork.Clear))]
    static class PriorityWorkClearNoInterface
    {
        // This can get called in the UI but has side effects
        static bool Prefix(PriorityWork __instance)
        {
            return Multiplayer.Client == null || Multiplayer.ExecutingCmds;
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.Notify_GenesChanged))]
    static class CheckWhetherBiotechIsActive
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand as MethodInfo == AccessTools.PropertyGetter(typeof(ModLister), nameof(ModLister.BiotechInstalled)))
                    inst.operand = AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.BiotechActive));
                yield return inst;
            }
        }
    }

    [HarmonyPatch]
    static class UpdateWorldStateWhenTickingOnly
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            // Handles relation and retaliation for polluting the world
            yield return AccessTools.DeclaredMethod(typeof(CompDissolutionEffect_Goodwill), nameof(CompDissolutionEffect_Goodwill.WorldUpdate));
            // Handles increasing/decreasing world pollution
            yield return AccessTools.DeclaredMethod(typeof(CompDissolutionEffect_Pollution), nameof(CompDissolutionEffect_Pollution.WorldUpdate));
        }

        static bool Prefix()
        {
            // In MP only allow updates from MultiplayerWorldComp:Tick()
            return Multiplayer.Client == null || AsyncWorldTimeComp.tickingWorld;
        }
    }

    [HarmonyPatch(typeof(Pawn_RecordsTracker), nameof(Pawn_RecordsTracker.ExposeData))]
    static class RecordsTrackerExposePatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var battleActiveField =
                AccessTools.Field(typeof(Pawn_RecordsTracker), nameof(Pawn_RecordsTracker.battleActive));

            foreach (var inst in insts)
            {
                // Remove mutation of battleActive during saving which was a source of non-determinism
                if (inst.opcode == OpCodes.Stfld && inst.operand as FieldInfo == battleActiveField)
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Pop);
                }
                else
                    yield return inst;
            }
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.CheckRecalculateSocialThoughts))]
    static class DontRecalculateSocialThoughtsInInterface
    {
        static bool Prefix(SituationalThoughtHandler __instance, Pawn otherPawn)
        {
            if (Multiplayer.Client == null) return true;
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds) return true;

            // This initializer needs to always run (the method itself begins with it)
            if (!__instance.cachedSocialThoughts.TryGetValue(otherPawn, out var value))
            {
                value = new SituationalThoughtHandler.CachedSocialThoughts();
                __instance.cachedSocialThoughts.Add(otherPawn, value);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.AppendSocialThoughts))]
    static class DontUpdateThoughtQueryTickInInterface
    {
        private static FieldInfo queryTickField = AccessTools.Field(typeof(SituationalThoughtHandler.CachedSocialThoughts), nameof(SituationalThoughtHandler.CachedSocialThoughts.lastQueryTick));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.opcode == OpCodes.Stfld && inst.operand as FieldInfo == queryTickField)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DontUpdateThoughtQueryTickInInterface), nameof(NewQueryTick)));
                }

                yield return inst;
            }
        }

        private static int NewQueryTick(int ticks, SituationalThoughtHandler thoughtHandler, Pawn otherPawn)
        {
            return Multiplayer.Client == null || Multiplayer.Ticking || Multiplayer.ExecutingCmds ?
                ticks :
                thoughtHandler.cachedSocialThoughts[otherPawn].lastQueryTick;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.UpdateAllMoodThoughts))]
    static class DontRecalculateMoodThoughtsInInterface
    {
        static bool Prefix(SituationalThoughtHandler __instance)
        {
            if (Multiplayer.Client != null && !Multiplayer.Ticking && !Multiplayer.ExecutingCmds) return false;

            // Notify_SituationalThoughtsDirty was called
            if (__instance.thoughtsDirty)
                __instance.cachedThoughts.Clear();

            return true;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.Notify_SituationalThoughtsDirty))]
    static class NotifyThoughtsDirtyPatch
    {
        private static MethodInfo clearMethod =
            AccessTools.Method(typeof(List<Thought_Situational>), nameof(List<Thought_Situational>.Clear));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand as MethodInfo == clearMethod)
                    yield return new CodeInstruction(OpCodes.Pop);
                else
                    yield return inst;
            }
        }
    }

    [HarmonyPatch(typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.GetLevel))]
    static class PawnCapacitiesHandlerGetLevelPatch
    {
        private static readonly PawnCapacitiesHandler.CacheStatus CachedInInterface = (PawnCapacitiesHandler.CacheStatus)3;

        private static FieldInfo statusField = AccessTools.Field(typeof(PawnCapacitiesHandler.CacheElement),
            nameof(PawnCapacitiesHandler.CacheElement.status));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var matcher = new CodeMatcher(insts);

            // Modify cache update checking
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Ldfld, statusField),
                new CodeMatch(OpCodes.Brtrue_S)
            ).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(PawnCapacitiesHandlerGetLevelPatch), nameof(ShouldUpdateCache))),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Ceq)
            );

            // Modify status setter
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Ldc_I4_2),
                new CodeMatch(OpCodes.Stfld, statusField)
            ).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(PawnCapacitiesHandlerGetLevelPatch), nameof(NewCacheStatus)))
            );

            return matcher.Instructions();
        }

        private static bool ShouldUpdateCache(PawnCapacitiesHandler.CacheStatus status)
        {
            return status == PawnCapacitiesHandler.CacheStatus.Uncached || !Multiplayer.InInterface && status == CachedInInterface;
        }

        private static PawnCapacitiesHandler.CacheStatus NewCacheStatus(PawnCapacitiesHandler.CacheStatus _)
        {
            return Multiplayer.InInterface ? CachedInInterface : PawnCapacitiesHandler.CacheStatus.Cached;
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), typeof(Thing), typeof(bool), typeof(int))]
    static class StatWorkerGetValuePatch
    {
        private static readonly PawnCapacitiesHandler.CacheStatus CachedInInterface = (PawnCapacitiesHandler.CacheStatus)3;

        private static FieldInfo statusField = AccessTools.Field(typeof(PawnCapacitiesHandler.CacheElement),
            nameof(PawnCapacitiesHandler.CacheElement.status));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var matcher = new CodeMatcher(insts);

            // Modify cache update checking
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Callvirt, typeof(Dictionary<Thing, StatCacheEntry>).GetMethod("TryGetValue"))
            ).Advance(1).Insert(
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(StatWorkerGetValuePatch), nameof(HasValueInCache)))
            );

            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Ldfld, typeof(StatCacheEntry).GetField(nameof(StatCacheEntry.gameTick)))
            ).Advance(1).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(Math), nameof(Math.Abs), new[] { typeof(int) }))
            );

            // Modify status setter
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Newobj)
            ).Advance(1).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(StatWorkerGetValuePatch), nameof(NewCacheTicksCtor)))
            );

            // Modify status setter
            matcher.MatchEndForward(
                    new CodeMatch(OpCodes.Stfld, typeof(StatCacheEntry).GetField(nameof(StatCacheEntry.gameTick)))
            ).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(StatWorkerGetValuePatch), nameof(NewCacheTicks)))
            );

            return matcher.Instructions();
        }

        private static bool HasValueInCache(bool hasValueInCache, StatWorker worker, Thing t)
        {
            var simulating = !Multiplayer.InInterface;
            return hasValueInCache && !(simulating && worker.temporaryStatCache[t].gameTick < 0);
        }

        private static StatCacheEntry NewCacheTicksCtor(StatCacheEntry entry)
        {
            entry.gameTick = NewCacheTicks(entry.gameTick);
            return entry;
        }

        private static int NewCacheTicks(int gameTick)
        {
            return Multiplayer.InInterface ? -gameTick : gameTick;
        }
    }

    [HarmonyPatch(typeof(GameConditionManager.MapBrightnessTracker), nameof(GameConditionManager.MapBrightnessTracker.Tick))]
    static class MapBrightnessLerpPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr)
        {
            var patchCount = 0;

            // The method is using delta time for darkness changes,
            // which is not good for MP since it's tied to the FPS.
            var target = AccessTools.DeclaredPropertyGetter(typeof(Time), nameof(Time.deltaTime));

            foreach (var ci in instr)
            {
                if (ci.Calls(target))
                {
                    // Replace deltaTime with a constant value.
                    // We use 1/60 since 1 second at speed 1 the deltaTime
                    // should (in perfect situation) be 60 ticks.
                    ci.opcode = OpCodes.Ldc_R4;
                    ci.operand = 1f / GenTicks.TicksPerRealSecond;

                    patchCount++;
                }

                yield return ci;
            }

            const int expectedPatches = 1;
            if (patchCount != expectedPatches)
                Log.Error($"Replaced an incorrect amount of Time.deltaTime calls for GameConditionManager.MapBrightnessTracker:Tick (expected: {expectedPatches}, patched: {patchCount}). Was the original method changed?");
        }
    }

    [HarmonyPatch(typeof(UndercaveMapComponent), nameof(UndercaveMapComponent.MapComponentTick))]
    static class DeterministicUndercaveRockCollapse
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr)
        {
            var target = MethodOf.Lambda(Rand.MTBEventOccurs);

            foreach (var ci in instr)
            {
                yield return ci;

                // Add "& false" to any call to Rand.MTBEventOccurs.
                // We'll handle those calls in our postfix.
                if (ci.Calls(target))
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }

        static void Prefix() => Rand.PushState();

        static void Postfix(UndercaveMapComponent __instance)
        {
            // Pop the RNG state from the prefix
            Rand.PopState();

            // Make sure the pit gate is collapsing
            if (__instance.pitGate is not { IsCollapsing: true })
                return;

            // Check if the rocks should collapse
            var mtb = UndercaveMapComponent.HoursToShakeMTBTicksCurve.Evaluate(__instance.pitGate.TicksUntilCollapse / 2500f);
            if (!Rand.MTBEventOccurs(mtb, 1, 1))
                return;

            // Since the number of RNG calls will depend on numDustEffecters argument, we need to push/pop the RNG state.
            // The RNG calls related to simulation will happen first, followed by the one determined by amount of
            // effecters - it would not be MP safe, but since it happens last it will be fine once we pop the state.
            Rand.PushState();

            // If not looking at the map, trigger the collapse without shake/effecters (since it's not needed for current player).
            // The call to play a sound is handled by RW itself, since it targets a specific map already.
            if (Find.CurrentMap != __instance.map)
            {
                // Progress the RNG state, matching the RandomInRange call in other two cases
                Rand.RangeInclusive(0, 100);
                __instance.TriggerCollapseFX(0, 0);
            }
            // Else, follow vanilla shake/effecter rules
            else if (__instance.pitGate.CollapseStage == 1)
                __instance.TriggerCollapseFX(UndercaveMapComponent.StageOneShakeAmount, UndercaveMapComponent.StageOneNumCollapseEffects.RandomInRange);
            else
                __instance.TriggerCollapseFX(UndercaveMapComponent.StageTwoShakeAmount, UndercaveMapComponent.StageTwoNumCollapseEffects.RandomInRange);

            Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote))]
    [HarmonyPatch([typeof(Vector3), typeof(Map), typeof(ThingDef), typeof(float), typeof(bool), typeof(float)])]
    static class FixNullMotes
    {
        // Make sure that motes will (almost) always spawn. We skip based to player-specific
        // data, and instead only allow the only fully deterministic checks (location is in map bounds).
        // We skip checks to current map and current mote counter saturation, as those may vary between players.

        static void Prefix(ref bool makeOffscreen)
        {
            // Skip a call to GenView.ShouldSpawnMotesAt, forcing
            // each call to check GenGrid.InBounds instead.
            // ShouldSpawnMotesAt would check for bounds, but also
            // include a current map check as well, which we don't want.
            makeOffscreen = true;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr)
        {
            var targetCall = AccessTools.DeclaredPropertyGetter(typeof(MoteCounter), nameof(MoteCounter.Saturated));

            foreach (var ci in instr)
            {
                yield return ci;

                // Add "& false" to any call to MoteCounter.Saturated to get a deterministic result.
                // Not a perfect solution performance-wise, but will prevent desyncs due to
                // MoteMaker.MakeStaticMote being non-deterministic without this change.
                if (ci.Calls(targetCall))
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Building_BioferriteHarvester), nameof(Building_BioferriteHarvester.SpawnSetup))]
    static class AlwaysRebuildBioferriteHarvesterCables
    {
        static void Postfix(Building_BioferriteHarvester __instance)
        {
            // After reloading the game the initialize method will generally be called
            // during rendering, ensure it's called on respawning as well.
            if (!__instance.initalized)
            {
                // We need to call ExecuteWhenFinished, as it will crash otherwise.
                LongEventHandler.ExecuteWhenFinished(__instance.Initialize);
            }
        }
    }

    [HarmonyPatch(typeof(Building_Electroharvester), nameof(Building_Electroharvester.SpawnSetup))]
    static class AlwaysRebuildElectroharvesterCables
    {
        static void Postfix(Building_Electroharvester __instance)
        {
            // After reloading the game the initialize method will generally be called
            // during rendering, ensure it's called on respawning as well.
            if (!__instance.initalized)
            {
                // We need to call ExecuteWhenFinished, as it will crash otherwise.
                LongEventHandler.ExecuteWhenFinished(__instance.Initialize);
            }
        }
    }

    // PathGridDoorsBlockedJob is a cross-tick Unity Job: scheduled at end of MapPreTick(N),
    // completes at start of MapPreTick(N+1). During MapTick(N) it runs concurrently on a
    // worker thread and reads live pawn.Position while the main thread writes
    // pawn.Position = nextCell in Pawn_PathFollower.PatherTick(). Host and client see
    // different enemy positions depending on thread scheduling → different blocked cells →
    // different A* path → different nextCell for the pathing pawn → WillCollideNextCell
    // differs → one client enters the attack branch → ChooseMeleeVerb → Rand.Chance → desync.
    //
    // Fix: snapshot all cached pawn positions on the main thread right after
    // ScheduleBatchedPathJobs (still inside MapPreTick, before any pawn ticking).
    // Worker threads reading pawn.Position during Execute() get the stable snapshot.
    static class PawnPositionSnapshot
    {
        // Written once per tick on the main thread before jobs are dispatched;
        // read concurrently by worker threads. Dictionary is safe for concurrent reads.
        static readonly Dictionary<Pawn, IntVec3> snapshot = new();

        public static void Rebuild(IReadOnlyList<Pawn> pawns)
        {
            snapshot.Clear();
            foreach (var pawn in pawns)
                snapshot[pawn] = pawn.Position;
        }

        public static bool TryGet(Pawn pawn, out IntVec3 pos) =>
            snapshot.TryGetValue(pawn, out pos);
    }

    [HarmonyPatch(typeof(PathFinder), "ScheduleBatchedPathJobs")]
    static class ScheduleBatchedPathJobsSnapshotPositions
    {
        static readonly FieldInfo CachedPawns =
            AccessTools.Field(typeof(PathFinder), "cachedPawns");

        static void Prefix(PathFinder __instance)
        {
            if (Multiplayer.Client == null) return;
            // Snapshot pawn positions immediately before PathGridDoorsBlockedJob is
            // dispatched so worker threads read stable positions instead of live ones.
            PawnPositionSnapshot.Rebuild((IReadOnlyList<Pawn>)CachedPawns.GetValue(__instance));
        }
    }

    [HarmonyPatch]
    static class PathGridDoorsBlockedJobPositionPatch
    {
        static readonly MethodInfo PositionGetter =
            AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Position));

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PathGridDoorsBlockedJob), "Execute");
            yield return AccessTools.Method(typeof(PathGridDoorsBlockedJob), "CanBlockEver");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.Calls(PositionGetter))
                    inst.operand = AccessTools.Method(
                        typeof(PathGridDoorsBlockedJobPositionPatch), nameof(GetPosition));
                yield return inst;
            }
        }

        internal static IntVec3 GetPosition(Thing thing)
        {
            if (Multiplayer.Client != null
                && thing is Pawn pawn
                && PawnPositionSnapshot.TryGet(pawn, out IntVec3 pos))
                return pos;
            return thing.Position;
        }
    }

    [HarmonyPatch(typeof(MainTabWindow), nameof(MainTabWindow.SetInitialSizeAndPosition))]
    static class MainTabWindow_NoResizingInSimulation
    {
        static bool Prefix()
        {
            return Multiplayer.Client == null || Multiplayer.InInterface;
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.DoWindowContents))]
    static class MainTabWindow_ResizeIfDirty
    {
        static void Prefix(MainTabWindow_PawnTable __instance)
        {
            if (__instance.table.dirty)
                __instance.SetInitialSizeAndPosition();
        }
    }

    [HarmonyPatch(typeof(PawnsArrivalModeWorker_EmergeFromWater), nameof(PawnsArrivalModeWorker_EmergeFromWater.Arrive))]
    static class PawnsArrivalModeWorker_EmergeFromWater_FixCurrentMapUsage
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr)
        {
            // Due to an oversight, the vanilla method is using Find.CurrentMap rather than using (Map)parms.target.
            // This causes bugs in the game, like mechanoids emerging from void/sand/random locations rather than water.
            // For us, it currently causes desyncs with multiple maps active, so we need to fix it.

            var currentMapMethod = typeof(Find).DeclaredPropertyGetter(nameof(Find.CurrentMap));
            var targetField = typeof(IncidentParms).DeclaredField(nameof(IncidentParms.target));

            var patched = 0;

            foreach (var ci in instr)
            {
                yield return ci;

                // Replace the Find.CurrentMap calls with (Map)parms.target
                if (ci.Calls(currentMapMethod))
                {
                    // Change the current instruction to load the 2nd argument (IncidentParms)
                    ci.opcode = OpCodes.Ldarg_2;
                    ci.operand = null;

                    // Load the IncidentParms.target field
                    yield return new CodeInstruction(OpCodes.Ldfld, targetField);

                    // Cast the field's value to Map
                    yield return new CodeInstruction(OpCodes.Castclass, typeof(Map));

                    patched++;
                }
            }

            const int expectedPatches = 2;
            if (patched != expectedPatches)
                Log.Error($"Patching emerge from water arrival mode failed. Expected patches: {expectedPatches}, actual patches: {patched}. There was either an issue or the bug was fixed in RimWorld itself.");
        }
    }

}
