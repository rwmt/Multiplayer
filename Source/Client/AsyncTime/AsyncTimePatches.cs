using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Factions;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.AsyncTime
{
    [HarmonyPatch]
    static class CancelMapManagersTick
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapPreTick));
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapPostTick));
        }

        static bool Prefix() => Multiplayer.Client == null || AsyncTimeComp.tickingMap != null;
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.AutosaverTick))]
    static class DisableAutosaver
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    static class MapUpdateMarker
    {
        public static bool updating;

        static void Prefix() => updating = true;
        static void Finalizer() => updating = false;
    }

    [HarmonyPatch]
    static class CancelMapManagersUpdate
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PowerNetManager), nameof(PowerNetManager.UpdatePowerNetsAndConnections_First));
            yield return AccessTools.Method(typeof(GlowGrid), nameof(GlowGrid.GlowGridUpdate_First));
            yield return AccessTools.Method(typeof(RegionGrid), nameof(RegionGrid.UpdateClean));
            yield return AccessTools.Method(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms));
        }

        static bool Prefix() => Multiplayer.Client == null || !MapUpdateMarker.updating;
    }

    // Season notifications run in the synced world tick and write saved state (lastSeason, letters), so they can't
    // depend on the local player's faction. In multifaction every player faction gets its own pass under its context
    // (which also routes its letters to its players) with its own saved lastSeason. Each pass uses the clock of the
    // faction's home map with the minimum time zone, the map vanilla takes the season from.
    [HarmonyPatch(typeof(DateNotifier), nameof(DateNotifier.DateNotifierTick))]
    static class DateNotifierPatch
    {
        static bool running;

        static bool Prefix(DateNotifier __instance)
        {
            if (Multiplayer.Client == null || running) return true;

            if (!Multiplayer.GameComp.multifaction)
            {
                // Every player plays the host's faction, the world tick runs under it
                TickWithHomeTime(__instance, requireHome: false);
                return false;
            }

            var worldComp = Multiplayer.WorldComp;
            foreach (var (factionId, _) in worldComp.factionData)
            {
                var faction = Find.FactionManager.GetById(factionId);
                if (faction is not { IsPlayer: true } || faction == worldComp.spectatorFaction)
                    continue;

                var prevLastSeason = __instance.lastSeason;
                __instance.lastSeason = worldComp.factionLastSeasons.TryGetValue(factionId, out var s) ? s : Season.Undefined;
                ((Map)null).PushFaction(faction, force: true);

                try
                {
                    // A faction without a home yet (e.g. just created) would take the season at 0,0 and could get a
                    // notification when it settles
                    TickWithHomeTime(__instance, requireHome: true);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception in DateNotifierTick for {faction}: {e}");
                }
                finally
                {
                    FactionExtensions.PopFaction();
                    worldComp.factionLastSeasons[factionId] = __instance.lastSeason;
                    __instance.lastSeason = prevLastSeason;
                }
            }

            return false;
        }

        static void TickWithHomeTime(DateNotifier notifier, bool requireHome)
        {
            Map map = notifier.FindPlayerHomeWithMinTimezone();
            if (map == null && requireHome) return;

            int prevTicks = Find.TickManager.TicksGame;
            if (map != null)
                Find.TickManager.DebugSetTicksGame(map.AsyncTime().mapTicks);

            running = true;
            try
            {
                notifier.DateNotifierTick();
            }
            finally
            {
                running = false;
                Find.TickManager.DebugSetTicksGame(prevTicks);
            }
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.RegisterAllTickabilityFor))]
    public static class TickListAdd
    {
        static bool Prefix(Thing t)
        {
            if (Multiplayer.Client == null || t.Map == null) return true;

            AsyncTimeComp comp = t.Map.AsyncTime();
            TickerType tickerType = t.def.tickerType;

            if (t is IThingHolder || tickerType == TickerType.Normal)
                comp.tickListNormal.RegisterThing(t);
            else if (tickerType == TickerType.Rare)
                comp.tickListRare.RegisterThing(t);
            else if (tickerType == TickerType.Long)
                comp.tickListLong.RegisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DeRegisterAllTickabilityFor))]
    public static class TickListRemove
    {
        static bool Prefix(Thing t)
        {
            if (Multiplayer.Client == null || t.Map == null) return true;

            AsyncTimeComp comp = t.Map.AsyncTime();
            TickerType tickerType = t.def.tickerType;

            if (tickerType == TickerType.Normal)
                comp.tickListNormal.DeregisterThing(t);
            else if (tickerType == TickerType.Rare)
                comp.tickListRare.DeregisterThing(t);
            else if (tickerType == TickerType.Long)
                comp.tickListLong.DeregisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    static class PreDrawCalcMarker
    {
        public static Pawn calculating;

        static void Prefix(PawnTweener __instance) => calculating = __instance.pawn;
        static void Finalizer() => calculating = null;
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    static class TickRateMultiplierPatch
    {
        static void Postfix(ref float __result)
        {
            if (PreDrawCalcMarker.calculating == null) return;
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldSelected) return;

            var map = PreDrawCalcMarker.calculating.Map ?? Find.CurrentMap;
            var asyncTime = map.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.DesiredTimeSpeed;

            __result = TickPatch.Simulating ? 6 : asyncTime.ActualRateMultiplier(timeSpeed);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Paused), MethodType.Getter)]
    static class TickManagerPausedPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldSelected) return;
            if (FactionCreator.generatingMap) return;

            var asyncTime = Find.CurrentMap.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.DesiredTimeSpeed;

            __result = asyncTime.ActualRateMultiplier(timeSpeed) == 0;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Notify_GeneratedPotentiallyHostileMap))]
    static class GeneratedHostileMapPatch
    {
        static bool Prefix() => Multiplayer.Client == null;

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            // The newly generated map
            Find.Maps.LastOrDefault()?.AsyncTime().slower.SignalForceNormalSpeedShort();
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), typeof(Letter), typeof(string), typeof(int), typeof(bool))]
    static class ReceiveLetterPause
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand as MethodInfo == AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.AutomaticPauseMode)))
                    inst.operand = AccessTools.Method(typeof(ReceiveLetterPause), nameof(AutomaticPauseMode));
                else if (inst.operand as MethodInfo == AccessTools.Method(typeof(TickManager), nameof(TickManager.Pause)))
                    inst.operand = AccessTools.Method(typeof(ReceiveLetterPause), nameof(PauseOnLetter));

                yield return inst;
            }
        }

        private static AutomaticPauseMode AutomaticPauseMode()
        {
            return Multiplayer.Client != null
                ? (AutomaticPauseMode)Multiplayer.GameComp.pauseOnLetter
                : Prefs.AutomaticPauseMode;
        }

        private static void PauseOnLetter(TickManager manager)
        {
            if (Multiplayer.Client == null)
            {
                manager.Pause();
                return;
            }

            if (Multiplayer.GameComp.asyncTime)
            {
                var tickable = (ITickable)Multiplayer.MapContext.AsyncTime() ?? Multiplayer.AsyncWorldTime;
                tickable.DesiredTimeSpeed = TimeSpeed.Paused;
                Multiplayer.GameComp.ResetAllTimeVotes(tickable.TickableId);
            }
            else
            {
                Multiplayer.AsyncWorldTime.SetTimeEverywhere(TimeSpeed.Paused);
                foreach (var tickable in TickPatch.AllTickables)
                    Multiplayer.GameComp.ResetAllTimeVotes(tickable.TickableId);
            }
        }
    }
}
