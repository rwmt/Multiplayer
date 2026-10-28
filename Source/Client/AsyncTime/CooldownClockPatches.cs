using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.AsyncTime
{
    // #964: async time stamps cooldowns under one map's clock and compares them
    // under another's after map/world transfer (world context never swaps the
    // clock at all). Fix: stamp and read against the world clock — it runs at
    // the fastest map's speed and pauses only when everything pauses, so it's a
    // monotone session clock defined in every context. Lockstep (non-async MP)
    // makes this an identity transform.
    static class CooldownClock
    {
        public static int Now => Multiplayer.AsyncWorldTime.worldTicks;

        // game/comp can lag Client during the join+load window
        public static bool Active => Multiplayer.Client != null && Multiplayer.game?.asyncWorldTimeComp != null;
    }

    // Vanilla Ability: cooldownEndTick's only readers all funnel through the
    // CooldownTicksRemaining getter (gizmo disable, tick-side end check,
    // per-charge recharge), so rebasing the stamp and the getter covers every
    // path, including Psycast subclasses and cooldownPerCharge.
    [HarmonyPatch(typeof(Ability), nameof(Ability.StartCooldown))]
    static class AbilityCooldownWorldClockStamp
    {
        static void Postfix(Ability __instance, int ticks)
        {
            if (CooldownClock.Active)
                __instance.cooldownEndTick = CooldownClock.Now + ticks;
        }
    }

    [HarmonyPatch(typeof(Ability), nameof(Ability.CooldownTicksRemaining), MethodType.Getter)]
    static class AbilityCooldownWorldClockRead
    {
        static bool Prefix(Ability __instance, ref int __result)
        {
            if (!CooldownClock.Active) return true;

            __result = __instance.inCooldown
                ? Mathf.Max(__instance.cooldownEndTick - CooldownClock.Now, 0)
                : 0;
            return false;
        }
    }

    // Daily skill growth: the midnight reset is gated by a 30k-tick window
    // against TicksGame. Across maps (or in caravan context, where the clock is
    // whatever map ticked last) the window goes incoherent and the reset is
    // skipped or fires early — saturation sticks. Body replicated from
    // 1.6.4871 with the guard and stamp moved to the world clock; the pawn's
    // local-midnight gate and per-skill Interval loop are untouched.
    [HarmonyPatch(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.SkillsTickInterval))]
    static class SkillDailyResetWorldClock
    {
        static bool Prefix(Pawn_SkillTracker __instance, int delta)
        {
            if (!CooldownClock.Active) return true;

            var pawn = __instance.pawn;
            if (!pawn.IsHashIntervalTick(200, delta) || !CanGainXP(pawn))
                return false;

            if (GenLocalDate.HourInteger(pawn) == 0 &&
                (__instance.lastXpSinceMidnightResetTimestamp < 0 ||
                 CooldownClock.Now - __instance.lastXpSinceMidnightResetTimestamp >= 30000))
            {
                for (int i = 0; i < __instance.skills.Count; i++)
                    __instance.skills[i].xpSinceMidnight = 0f;

                __instance.lastXpSinceMidnightResetTimestamp = CooldownClock.Now;
            }

            for (int j = 0; j < __instance.skills.Count; j++)
                __instance.skills[j].Interval();

            return false;
        }

        // Pawn_SkillTracker.CanGainXP is private; replicated (1.6.4871)
        static bool CanGainXP(Pawn pawn)
        {
            if (ModsConfig.AnomalyActive && pawn.IsMutant && !pawn.mutant.Def.canGainXP)
                return false;
            return true;
        }
    }

    // Shuttle cooldown: the shuttle physically leaves the stamping map, so the
    // stamp is rebased at the write sites and the compare sites read under the
    // world clock. TryLaunch is NOT wrapped in a clock swap — it spawns
    // skyfallers and motes that must keep stamping their own map ticks.
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch))]
    static class LaunchCooldownWorldClockStamp
    {
        static void Prefix(CompLaunchable __instance, ref int __state)
            => __state = __instance.lastLaunchTick;

        static void Postfix(CompLaunchable __instance, int __state)
        {
            // Rebase only if this call actually stamped (TryLaunch has
            // early-out paths that leave the field untouched)
            if (CooldownClock.Active && __instance.lastLaunchTick != __state)
                __instance.lastLaunchTick = CooldownClock.Now;
        }
    }

    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.Notify_Arrived))]
    static class LaunchCooldownArrivalWorldClockStamp
    {
        static void Postfix(CompLaunchable __instance)
        {
            if (CooldownClock.Active)
                __instance.lastLaunchTick = CooldownClock.Now;
        }
    }

    // Read swaps: both methods are read-only w.r.t. game state, so swapping
    // ticksGameInt for their duration only affects the cooldown compare. The
    // finalizer restores unconditionally, exception or not.
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.CanLaunch))]
    static class LaunchCooldownCanLaunchWorldClock
    {
        static void Prefix(ref int? __state)
        {
            if (!CooldownClock.Active) return;
            __state = Find.TickManager.TicksGame;
            Find.TickManager.DebugSetTicksGame(CooldownClock.Now);
        }

        static void Finalizer(int? __state)
        {
            if (__state is { } prev)
                Find.TickManager.DebugSetTicksGame(prev);
        }
    }

    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.CompInspectStringExtra))]
    static class LaunchCooldownInspectWorldClock
    {
        static void Prefix(ref int? __state)
        {
            if (!CooldownClock.Active) return;
            __state = Find.TickManager.TicksGame;
            Find.TickManager.DebugSetTicksGame(CooldownClock.Now);
        }

        static void Finalizer(int? __state)
        {
            if (__state is { } prev)
                Find.TickManager.DebugSetTicksGame(prev);
        }
    }

    // CompTick's only job here is the cooldown-ended message; its == compare
    // must run against the same clock the stamp uses. A slow map can tick past
    // the exact world tick and miss the message — cosmetic, accepted.
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.CompTick))]
    static class LaunchCooldownEndedMessageWorldClock
    {
        static bool Prefix(CompLaunchable __instance)
        {
            if (!CooldownClock.Active) return true;

            if (!__instance.Props.cooldownEndedMessage.NullOrEmpty() &&
                __instance.lastLaunchTick > 0 &&
                __instance.lastLaunchTick + __instance.Props.cooldownTicks == CooldownClock.Now)
            {
                Messages.Message(__instance.Props.cooldownEndedMessage.Formatted(__instance.parent.LabelCap),
                    __instance.parent, MessageTypeDefOf.NeutralEvent, historical: false);
            }
            return false;
        }
    }
}
