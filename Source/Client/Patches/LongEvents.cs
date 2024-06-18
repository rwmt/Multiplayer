using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(Action))]
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
                Multiplayer.Client.Send(Packets.Client_Freeze, new object[] { true });
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteToExecuteWhenFinished))]
    static class LongEventEnd
    {
        static void Postfix()
        {
            if (Multiplayer.Client != null && NewLongEvent.currentEventWasMarked)
                Multiplayer.Client.Send(Packets.Client_Freeze, new object[] { false });
        }
    }

    [HarmonyPriority(Priority.HigherThanNormal)]
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(Action) })]
    static class LongEventAlwaysSync
    {
        public static bool forceSynchronously = false;

        static void Prefix(ref bool doAsynchronously, ref Action action, Action callback)
        {
            if (Multiplayer.ExecutingCmds || forceSynchronously)
            {
                doAsynchronously = false;

                // Callback is only called in asynchronous long events and will be skipped in
                // a synchronous ones. Make sure they are called after the actual action.
                // This patch also needs higher priority than SeedLongEvents
                // to make sure that both action and callback are seeded properly.
                if (callback != null)
                    action += callback;
            }
        }
    }
}
