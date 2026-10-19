using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch(nameof(Game.LoadGame))]
    public static class SeedGameLoad
    {
        static void Prefix()
        {
            Rand.PushState();

            if (Multiplayer.Client == null) return;
            Rand.Seed = 1;
        }

        static void Finalizer()
        {
            Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.ExposeData))]
    public static class SeedMapLoad
    {
        static void Prefix(Map __instance, ref bool __state)
        {
            if (Multiplayer.Client == null) return;
            if (Scribe.mode != LoadSaveMode.LoadingVars && Scribe.mode != LoadSaveMode.ResolvingCrossRefs && Scribe.mode != LoadSaveMode.PostLoadInit) return;

            int seed = Gen.HashCombineInt(__instance.uniqueID, Find.World.ConstantRandSeed);
            Rand.PushState(seed);

            __state = true;
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.FinalizeLoading))]
    public static class SeedMapFinalizeLoading
    {
        static void Prefix(Map __instance, ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            int seed = Gen.HashCombineInt(__instance.uniqueID, Find.World.ConstantRandSeed);
            Rand.PushState(seed);

            __state = true;
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter), typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool))]
    static class SeedCaravanEnter
    {
        static void Prefix(Map map, ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            int seed = Gen.HashCombineInt(map.uniqueID, Find.World.ConstantRandSeed);
            Rand.PushState(seed);

            __state = true;
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    // The seed and other simulation context are applied when the event runs, see SimLongEventContext
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(bool), typeof(Action))]
    static class SeedLongEvents
    {
        static void Prefix(string textKey, ref SimLongEventContext __state)
        {
            // MpSaving events (autosaves) are queued from the non-simulation part of ConstantTicker on some clients only
            if (SimLongEventContext.ShouldCapture && textKey != "MpSaving")
                __state = SimLongEventContext.Capture();
        }

        static void Postfix(Action action, SimLongEventContext __state)
        {
            if (__state == null) return;

            // Find the event that was just queued (it isn't if another prefix cancelled it)
            LongEventHandler.QueuedLongEvent queued = null;
            foreach (var ev in LongEventHandler.eventQueue)
                if (ReferenceEquals(ev.eventAction, action))
                    queued = ev;

            if (queued != null)
                SimLongEventContext.Attach(queued, __state);
        }
    }

    // Seed the rotation random
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool))]
    static class GenSpawnRotatePatch
    {
        static MethodInfo Rot4GetRandom = AccessTools.Property(typeof(Rot4), nameof(Rot4.Random)).GetGetMethod();

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                if (inst.operand as MethodInfo == Rot4GetRandom)
                {
                    // Load newThing.thingIdNumber to the stack
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Thing), nameof(Thing.thingIDNumber)));
                    // Load Find.World.ConstantRandSeed to the stack
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Find), nameof(Find.World)));
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(World), nameof(World.ConstantRandSeed)));
                    // Pop our 2 values, call Gen.HashCombineInt with them, and push the outcome to the stack
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Gen), nameof(Gen.HashCombineInt), [typeof(int), typeof(int)]));
                    // Pop the value off the stack and call Rand.PushState with it as the argument
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Rand), nameof(Rand.PushState), [typeof(int)]));
                }

                yield return inst;

                if (inst.operand as MethodInfo == Rot4GetRandom)
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Rand), nameof(Rand.PopState)));
            }
        }
    }

    public static class RandPatches
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        public static void Prefix(ref bool __state)
        {
            Rand.PushState();
            __state = true;
        }

        // For methods that may also run on worker threads (e.g. async long events), where touching the shared
        // Rand state stack would race with the main thread
        [HarmonyPriority(MpPriority.MpFirst)]
        public static void PrefixMainThread(ref bool __state)
        {
            if (!UnityData.IsInMainThread) return;
            Rand.PushState();
            __state = true;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        public static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    /// <summary>
    /// For code inside a tick or command which runs only on some clients, e.g. a window opened only for the issuer of
    /// a command or local UI updates. Keeps it off the shared RNG stream, unique id counters and desync traces.
    /// </summary>
    public readonly struct LocalOnlyScope : IDisposable
    {
        private readonly bool prevLocalIds;

        private LocalOnlyScope(bool prevLocalIds) => this.prevLocalIds = prevLocalIds;

        public static LocalOnlyScope Enter()
        {
            var scope = new LocalOnlyScope(Patches.UniqueIdsPatch.useLocalIdsOverride);

            Rand.PushState();
            Desyncs.DeferredStackTracing.ignoreTraces++;
            Patches.UniqueIdsPatch.useLocalIdsOverride = true;

            return scope;
        }

        public void Dispose()
        {
            Patches.UniqueIdsPatch.useLocalIdsOverride = prevLocalIds;
            Desyncs.DeferredStackTracing.ignoreTraces--;
            Rand.PopState();
        }
    }

    [HarmonyPatch]
    static class SeedGrammar
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GrammarResolver), nameof(GrammarResolver.Resolve));
            yield return AccessTools.Method(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.GeneratePawnName));
            yield return AccessTools.Method(typeof(NameGenerator), nameof(NameGenerator.GenerateName), [typeof(RulePackDef), typeof(Predicate<string>), typeof(bool), typeof(string), typeof(string), typeof(List<Rule>)]);
            // Called directly by quest, ideo, deity and unique weapon naming; its validator retries depend on the
            // generated text, so on the game language
            yield return AccessTools.Method(typeof(NameGenerator), nameof(NameGenerator.GenerateName), [typeof(GrammarRequest), typeof(Predicate<string>), typeof(bool), typeof(string), typeof(string)]);
            // Retries whole GenerateName calls while the name collides with an existing quest's name
            yield return AccessTools.Method(typeof(QuestNode_ResolveQuestName), nameof(QuestNode_ResolveQuestName.Resolve));
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(ref bool __state)
        {
            Rand.Element(0, 0); // advance the rng
            Rand.PushState();
            __state = true;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch]
    static class SeedPawnGraphics
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return MpMethodUtil.GetLambda(typeof(PawnRenderer), nameof(PawnRenderer.SetAllGraphicsDirty), 0);
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(PawnRenderer __instance, ref bool __state)
        {
            Rand.PushState(Gen.HashCombineInt(__instance.pawn.thingIDNumber, Find.World.ConstantRandSeed));
            __state = true;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(PreceptComp_UnwillingToDo_Chance), nameof(PreceptComp_UnwillingToDo_Chance.MemberWillingToDo))]
    static class SeedPreceptComp_UnwillingToDo_Chance
    {
        public static void Prefix(ref bool __state, HistoryEvent ev)
        {
            if (Multiplayer.Client == null) return;

            // PreceptComp_UnwillingToDo_Chance.MemberWillingToDo uses RNG, and can be called in interface (or other places that cause issues).
            // Seed the RNG using the pawn's ID (or the world's constant rand seed as fallback) and the current tick. This will ensure we
            // get a unique result for each pawn on a given tick, but if called multiple times on the same time it'll have a consistent result.
            // The fallback is mostly unnecessary, as this precept comp expects a doer. However, we may as well include it as a precaution.
            // We could add some more parameters to the seed, like using the map's ID or the world's seed, but it's probably an overkill.
            // This could probably be handled in a smarter way, so if anyone has an idea on how and is willing to do it - go ahead and replace this.
            var pawnId = ev.args.TryGetArg<Pawn>(HistoryEventArgsNames.Doer, out var pawn) ? pawn.thingIDNumber : Find.World.ConstantRandSeed;

            Rand.PushState(Gen.HashCombineInt(pawnId, Find.TickManager.TicksGame));
            __state = true;
        }

        public static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(RitualBehaviorWorker_GravshipLaunch), nameof(RitualBehaviorWorker_GravshipLaunch.CanReachGravship))]
    static class SeedGravshipCanReachGravship
    {
        static void Prefix(ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            // The gravship launch confirmation dialog works out which pawns can board while it is being
            // built: Dialog_BeginRitual's constructor calls CreateRitualRoleAssignments ->
            // RitualRoleAssignments.PawnNotAssignableReason -> RitualBehaviorWorker_GravshipLaunch
            // .PawnCanFillRole -> CanReachGravship -> GravshipUtility.TryFindSpotOnGravship ->
            // Region.RandomCell, which consumes RNG. Building that dialog is UI work and, because the
            // dialog is opened only by the issuing peer (see CancelDialogBeginRitual's
            // currentExecutingCmdIssuedBySelf gate), it runs on just one peer - so this RNG must not
            // advance the shared stream. Spamming the launch button repeats it and the divergence
            // accumulates into a desync ("Random state from commands doesn't match").
            // CanReachGravship returns a bool that does not depend on the RNG (it only searches for any
            // allowed cell), and the real boarding calls TryFindSpotOnGravship outside this check, so
            // isolating the RNG here leaves both the check result and determinism intact.
            Rand.PushState();
            __state = true;
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

}
