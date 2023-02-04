using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        static void Postfix() => updating = false;
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

    [HarmonyPatch(typeof(DateNotifier), nameof(DateNotifier.DateNotifierTick))]
    static class DateNotifierPatch
    {
        static void Prefix(DateNotifier __instance, ref int? __state)
        {
            if (Multiplayer.Client == null && Multiplayer.RealPlayerFaction != null) return;

            Map map = __instance.FindPlayerHomeWithMinTimezone();
            if (map == null) return;

            __state = Find.TickManager.TicksGame;
            FactionContext.Push(Multiplayer.RealPlayerFaction);
            Find.TickManager.DebugSetTicksGame(map.AsyncTime().mapTicks);
        }

        static void Postfix(int? __state)
        {
            if (!__state.HasValue) return;
            Find.TickManager.DebugSetTicksGame(__state.Value);
            FactionContext.Pop();
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

            if (tickerType == TickerType.Normal)
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
        static void Postfix() => calculating = null;
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    static class TickRateMultiplierPatch
    {
        static void Postfix(ref float __result)
        {
            if (PreDrawCalcMarker.calculating == null) return;
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldRenderedNow) return;

            var map = PreDrawCalcMarker.calculating.Map ?? Find.CurrentMap;
            var asyncTime = map.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.TimeSpeed;

            __result = TickPatch.Simulating ? 6 : asyncTime.ActualRateMultiplier(timeSpeed);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Paused), MethodType.Getter)]
    static class TickManagerPausedPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldRenderedNow) return;

            var asyncTime = Find.CurrentMap.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.TimeSpeed;

            __result = asyncTime.ActualRateMultiplier(timeSpeed) == 0;
        }
    }

    [HarmonyPatch]
    public class StorytellerTickPatch
    {
        public static bool updating;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Storyteller), nameof(Storyteller.StorytellerTick));
            yield return AccessTools.Method(typeof(StoryWatcher), nameof(StoryWatcher.StoryWatcherTick));
        }

        static bool Prefix()
        {
            updating = true;
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }

        static void Postfix() => updating = false;
    }

    [HarmonyPatch(typeof(Storyteller))]
    [HarmonyPatch(nameof(Storyteller.AllIncidentTargets), MethodType.Getter)]
    public class StorytellerTargetsPatch
    {
        static void Postfix(List<IIncidentTarget> __result)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.MapContext != null)
            {
                __result.Clear();
                __result.Add(Multiplayer.MapContext);
            }
            else if (MultiplayerWorldComp.tickingWorld)
            {
                __result.Clear();

                foreach (var caravan in Find.WorldObjects.Caravans)
                    if (caravan.IsPlayerControlled)
                        __result.Add(caravan);

                __result.Add(Find.World);
            }
        }
    }

    // The MP Mod's ticker calls Storyteller.StorytellerTick() on both the World and each Map, each tick
    // This patch aims to ensure each "spawn raid" Quest is only triggered once, to prevent 2x or 3x sized raids
    [HarmonyPatch(typeof(Quest), nameof(Quest.PartsListForReading), MethodType.Getter)]
    public class QuestPartsListForReadingPatch
    {
        static void Postfix(ref List<QuestPart> __result)
        {
            if (StorytellerTickPatch.updating)
            {
                __result = __result.Where(questPart => {
                    if (questPart is QuestPart_ThreatsGenerator questPartThreatsGenerator)
                    {
                        return questPartThreatsGenerator.mapParent?.Map == Multiplayer.MapContext;
                    }
                    return true;
                }).ToList();
            }
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

    [HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultParmsNow))]
    static class MapContextIncidentParms
    {
        static void Prefix(IIncidentTarget target, ref Map __state)
        {
            // This may be running inside a context already
            if (AsyncTimeComp.tickingMap != null)
                return;

            if (MultiplayerWorldComp.tickingWorld && target is Map map)
            {
                AsyncTimeComp.tickingMap = map;
                map.AsyncTime().PreContext();
                __state = map;
            }
        }

        static void Postfix(Map __state)
        {
            if (__state != null)
            {
                __state.AsyncTime().PostContext();
                AsyncTimeComp.tickingMap = null;
            }
        }
    }

    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    static class MapContextIncidentExecute
    {
        static void Prefix(IncidentParms parms, ref Map __state)
        {
            if (MultiplayerWorldComp.tickingWorld && parms.target is Map map)
            {
                AsyncTimeComp.tickingMap = map;
                map.AsyncTime().PreContext();
                __state = map;
            }
        }

        static void Postfix(Map __state)
        {
            if (__state != null)
            {
                __state.AsyncTime().PostContext();
                AsyncTimeComp.tickingMap = null;
            }
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), typeof(Letter), typeof(string))]
    static class ReceiveLetterPause
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.AutomaticPauseMode)))
                    inst.operand = AccessTools.Method(typeof(ReceiveLetterPause), nameof(AutomaticPauseMode));
                else if (inst.operand == AccessTools.Method(typeof(TickManager), nameof(TickManager.Pause)))
                    inst.operand = AccessTools.Method(typeof(ReceiveLetterPause), nameof(PauseOnLetter));

                yield return inst;
            }
        }

        static AutomaticPauseMode AutomaticPauseMode()
        {
            return Multiplayer.Client != null
                ? (AutomaticPauseMode) Multiplayer.GameComp.pauseOnLetter
                : Prefs.AutomaticPauseMode;
        }

        static void PauseOnLetter(TickManager manager)
        {
            if (Multiplayer.Client == null)
            {
                manager.Pause();
                return;
            }

            if (Multiplayer.GameComp.asyncTime)
            {
                var tickable = (ITickable)Multiplayer.MapContext.AsyncTime() ?? Multiplayer.WorldComp;
                tickable.TimeSpeed = TimeSpeed.Paused;
                Multiplayer.GameComp.ResetAllTimeVotes(tickable.TickableId);
                if (tickable is AsyncTimeComp comp)
                    comp.TrySetPrevTimeSpeed(TimeSpeed.Paused);
            }
            else
            {
                Multiplayer.WorldComp.SetTimeEverywhere(TimeSpeed.Paused);
                foreach (var tickable in TickPatch.AllTickables)
                {
                    Multiplayer.GameComp.ResetAllTimeVotes(tickable.TickableId);
                    if (tickable is AsyncTimeComp comp)
                        comp.TrySetPrevTimeSpeed(TimeSpeed.Paused);
                }
            }
        }
    }
}
