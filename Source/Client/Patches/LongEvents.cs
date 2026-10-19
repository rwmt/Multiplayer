using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent),
        typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(bool), typeof(Action))]
    static class MarkLongEvents
    {
        private static MethodInfo MarkerMethod = AccessTools.Method(typeof(MarkLongEvents), nameof(Marker));

        static void Prefix(ref Action action, string textKey)
        {
            if (Multiplayer.Client is { State: ConnectionStateEnum.ClientPlaying } && (Multiplayer.Ticking || Multiplayer.ExecutingCmds || textKey == "MpSaving"))
            {
                action += Marker;
            }
        }

        private static void Marker()
        {
        }

        public static bool IsTickMarked(Action action)
        {
            return action?.GetInvocationList()?.Any(d => d.Method == MarkerMethod) ?? false;
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    static class NewLongEvent
    {
        public static bool currentEventWasMarked;

        static void Prefix(ref bool __state)
        {
            __state = LongEventHandler.currentEvent == null;
            currentEventWasMarked = MarkLongEvents.IsTickMarked(LongEventHandler.currentEvent?.eventAction);
        }

        static void Postfix(bool __state)
        {
            currentEventWasMarked = false;

            if (Multiplayer.Client == null) return;

            if (__state && MarkLongEvents.IsTickMarked(LongEventHandler.currentEvent?.eventAction))
                Multiplayer.Client.Send(ClientFreezePacket.Freeze());
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteToExecuteWhenFinished))]
    static class LongEventEnd
    {
        static void Postfix()
        {
            if (Multiplayer.Client != null && NewLongEvent.currentEventWasMarked)
                Multiplayer.Client.Send(ClientFreezePacket.Unfreeze());
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), [typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(bool), typeof(Action)])]
    static class LongEventAlwaysSync
    {
        static void Prefix(ref bool doAsynchronously, ref Action action, Action callback)
        {
            // Asynchronous events would run on a worker thread sharing the global Rand with the UI
            if (SimLongEventContext.ShouldCapture)
            {
                doAsynchronously = false;

                // Callback is only called in asynchronous long events and will be skipped in
                // a synchronous ones. Make sure they are called after the actual action.
                if (callback != null)
                    action += callback;
            }
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.UpdateCurrentAsynchronousEvent))]
    public static class PatchUpdateCurrentAsynchronousEventCleanUpOrder
    {
        readonly static MethodInfo OrginalMethod = AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteToExecuteWhenFinished));
        readonly static MethodInfo ReplacementMethod = AccessTools.Method(typeof(PatchUpdateCurrentAsynchronousEventCleanUpOrder), nameof(ReorderCleanupCode));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(OrginalMethod))
                {
                    yield return new CodeInstruction(OpCodes.Call, ReplacementMethod);
                    yield return new CodeInstruction(OpCodes.Ret);
                }
                else
                    yield return instruction;
            }
        }

        public static void ReorderCleanupCode()
        {
            Action callback = LongEventHandler.currentEvent.callback;
            var context = SimLongEventContext.For(LongEventHandler.currentEvent);

            LongEventHandler.currentEvent = null;
            LongEventHandler.eventThread = null;
            LongEventHandler.levelLoadOp = null;

            context?.Enter();
            try
            {
                LongEventHandler.ExecuteToExecuteWhenFinished();

                if (callback != null)
                    callback();
            }
            finally
            {
                context?.Exit();
            }
        }
    }

    // Synchronous events run the action, then (with currentEvent already cleared) the actions deferred with
    // LongEventHandler.ExecuteWhenFinished. Both get the simulation context of an event queued from the simulation.
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.UpdateCurrentSynchronousEvent))]
    static class SimLongEventSynchronousContext
    {
        static void Prefix(ref SimLongEventContext __state)
        {
            __state = SimLongEventContext.For(LongEventHandler.currentEvent);
            __state?.Enter();
        }

        static void Finalizer(SimLongEventContext __state)
        {
            __state?.Exit();
        }
    }

    /// <summary>
    /// The simulation context of a long event queued during a tick or command. Long events run later, outside any
    /// tick or command, so this re-applies what has to be the same on all clients: a seeded RNG, the faction the
    /// event was queued for (multifaction) and a non-interface context. The latter matters for the actions the event
    /// defers with ExecuteWhenFinished: they run after currentEvent is cleared and would otherwise count as UI
    /// (local unique ids, sending sync commands).
    /// </summary>
    public class SimLongEventContext
    {
        private static readonly ConditionalWeakTable<LongEventHandler.QueuedLongEvent, SimLongEventContext> contexts = new();
        private static SimLongEventContext current;

        // A reload (e.g. a join point) replaces the game: what runs after it isn't simulation of the old game anymore
        public static bool InSimulation => current != null && current.game != null && current.game == Multiplayer.game;

        public static bool ShouldCapture =>
            Multiplayer.Client != null && (Multiplayer.Ticking || Multiplayer.ExecutingCmds || InSimulation);

        private readonly MultiplayerGame game;
        private readonly int seed;
        private readonly Faction faction;

        private SimLongEventContext prev;
        private bool pushedFaction;

        private SimLongEventContext(int seed, Faction faction)
        {
            game = Multiplayer.game;
            this.seed = seed;
            this.faction = faction;
        }

        // The seed is derived from the RNG state without advancing it: some events are queued only on the issuer of a
        // command (e.g. join points on a standalone server), which mustn't move the shared stream
        public static SimLongEventContext Capture()
        {
            ulong state = Rand.StateCompressed;
            int seed = Gen.HashCombineInt((int)state, (int)(state >> 32));
            return new(seed, Multiplayer.GameComp.multifaction ? Faction.OfPlayer : null);
        }

        public static void Attach(LongEventHandler.QueuedLongEvent ev, SimLongEventContext context)
        {
            contexts.Remove(ev);
            contexts.Add(ev, context);
        }

        public static SimLongEventContext For(LongEventHandler.QueuedLongEvent ev) =>
            ev != null && contexts.TryGetValue(ev, out var context) ? context : null;

        public void Enter()
        {
            prev = current;
            current = this;

            // Runs in a later frame than the tick or command which queued it, so it's a step of its own
            TickPatch.simStep++;

            Rand.PushState(seed);
            GravEngineCache.Reset();

            if (faction != null && game == Multiplayer.game)
            {
                pushedFaction = true;
                if (FactionContext.Push(faction, force: true) is { } f)
                    SetFactionData(f);
            }
        }

        public void Exit()
        {
            try
            {
                // FactionContext is cleared when the game is reloaded
                if (pushedFaction && game == Multiplayer.game && FactionContext.stack.Count > 0)
                {
                    FactionContext.Pop();

                    // Long events run in the interface context, where Faction.OfPlayer is the real faction. Restore
                    // that instead of the popped faction as the event may have changed it (FactionCreator).
                    if (Multiplayer.RealPlayerFaction is { } real)
                    {
                        FactionContext.Set(real);
                        SetFactionData(real);
                    }
                }
            }
            finally
            {
                pushedFaction = false;

                // A context left active would keep InInterface false for good
                current = prev;
                prev = null;

                Rand.PopState();
            }
        }

        private static void SetFactionData(Faction f)
        {
            Multiplayer.WorldComp?.SetFaction(f);
            foreach (var map in Find.Maps)
                map.MpComp().SetFaction(f);
        }
    }
}
