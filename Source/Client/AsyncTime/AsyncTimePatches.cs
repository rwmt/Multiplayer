using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Util;
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

    // Season notifications. The old patch pushed Multiplayer.RealPlayerFaction
    // and that client's own faction's map clock into a method that runs in the
    // synced world tick and writes scribed state (lastSeason) - per-client
    // inputs in the deterministic sim. Under async, clients' chosen maps cross
    // season boundaries at different world ticks and lastSeason diverges (the
    // "summer arrived twice" desync); a spectating client diverged even in
    // single-faction games. Replaced with a per-faction pass over synced state
    // only: each ownable player faction, in deterministic order, gets its
    // season computed from ITS min-timezone home map's async clock, with its
    // own scribed lastSeason (FactionWorldData) and its letters routed by the
    // pushed context. Identical inputs on every client by construction.
    [HarmonyPatch(typeof(DateNotifier), nameof(DateNotifier.DateNotifierTick))]
    static class DateNotifierPatch
    {
        static bool Prefix(DateNotifier __instance)
        {
            if (Multiplayer.Client == null)
                return true;

            foreach (var kv in Multiplayer.WorldComp.factionData)
            {
                var faction = Find.FactionManager.GetById(kv.Key);
                if (!QuestFactionOwnership.IsOwnablePlayerFaction(faction))
                    continue;

                ((Map)null).PushFaction(faction);
                try
                {
                    TickForFaction(__instance, kv.Value);
                }
                finally
                {
                    FactionExtensions.PopFaction();
                }
            }

            return false;
        }

        // Vanilla DateNotifierTick body (1.6.4871) against per-faction state,
        // under the faction's context and its map's clock
        static void TickForFaction(DateNotifier notifier, FactionWorldData data)
        {
            // First run on an existing save: adopt the global pre-fix state so
            // the transition already seen isn't re-announced
            if (data.lastSeason == Season.Undefined && notifier.lastSeason != Season.Undefined)
                data.lastSeason = notifier.lastSeason;

            Map map = notifier.FindPlayerHomeWithMinTimezone();

            int prevTicks = Find.TickManager.TicksGame;
            if (map != null)
                Find.TickManager.DebugSetTicksGame(map.AsyncTime().mapTicks);

            try
            {
                float latitude = map != null ? Find.WorldGrid.LongLatOf(map.Tile).y : 0f;
                float longitude = map != null ? Find.WorldGrid.LongLatOf(map.Tile).x : 0f;
                Season season = GenDate.Season(Find.TickManager.TicksAbs, latitude, longitude);

                if (season == data.lastSeason ||
                    (data.lastSeason != Season.Undefined && season == data.lastSeason.GetPreviousSeason()))
                    return;

                if (data.lastSeason != Season.Undefined && notifier.AnyPlayerHomeSeasonsAreMeaningful())
                {
                    if (GenDate.YearsPassed == 0 && season == Season.Summer &&
                        notifier.AnyPlayerHomeAvgTempIsLowInWinter())
                        Find.LetterStack.ReceiveLetter("LetterLabelFirstSummerWarning".Translate(),
                            "FirstSummerWarning".Translate(), LetterDefOf.NeutralEvent);
                    else if (GenDate.DaysPassed > 5)
                        Messages.Message("MessageSeasonBegun".Translate(season.Label()).CapitalizeFirst(),
                            MessageTypeDefOf.NeutralEvent);
                }

                data.lastSeason = season;
            }
            finally
            {
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
            if (Find.CurrentMap == null) return;

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
