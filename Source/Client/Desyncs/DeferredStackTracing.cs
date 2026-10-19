using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Multiplayer.Client.Patches;
using Multiplayer.Common;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Desyncs
{
    [EarlyPatch]
    [HarmonyPatch]
    public static class DeferredStackTracing
    {
        public static int ignoreTraces;
        public static long maxTraceDepth;
        public static int randCalls;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Value));
            yield return AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Int));
        }

        public static int acc;

        private static bool SupportsDeferredStackTracing =>
            RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86;

        private static bool loggedTraceFailure;

        // Set by the first exception thrown by the stack walk, until the game restarts. On some setups (e.g. macOS
        // through Rosetta) the walk throws on every Rand call, which stalls the game (#856). Traces are diagnostic
        // only: the window being built is marked unreliable below and this client's later windows have no trace
        // hashes, which aren't compared.
        public static bool tracingFailed;

        public static void Postfix()
        {
            if (Native.LmfPtr == 0) return;

            // This runs inside Rand and other game code: an exception here would abort the game code on this client
            // only, so never let one escape
            try
            {
                if (!ShouldAddStackTraceForDesyncLog()) return;

                var logItem = StackTraceLogItemRaw.GetFromPool();
                var trace = logItem.raw;
                int hash = 0;
                int depth;

                try
                {
                    // Skip this (DeferredStackTracing.Postfix) frame. Keeping it in the trace doesn't provide any value.
                    depth = DeferredStackTracingImpl.TraceImpl(trace, ref hash, skipFrames: 1);
                }
                catch (Exception e)
                {
                    // Mark first: if logging throws, the outer catch skips the rest of this method
                    tracingFailed = true;
                    Multiplayer.game.sync.MarkTracesUnreliable();

                    LogTraceFailureOnce(e.ToString());
                    Log.Warning("Multiplayer: desync stack traces are turned off until the game restarts");
                    depth = -1;
                }

                if (depth < 0)
                {
                    // Keep the trace count in line with other clients but don't compare this window's trace hashes
                    depth = 0;
                    hash = 0;
                    Multiplayer.game.sync.MarkTracesUnreliable();

                    if (DeferredStackTracingImpl.lastFailureReason != null)
                        LogTraceFailureOnce(DeferredStackTracingImpl.lastFailureReason);
                }

                Multiplayer.game.sync.TryAddStackTraceForDesyncLogRaw(logItem, depth, hash);

                acc++;
            }
            catch (Exception e)
            {
                try
                {
                    // Traces stop for this client mid-window, so its trace hashes can't be compared
                    if (tracingFailed)
                        Multiplayer.game?.sync.MarkTracesUnreliable();
                }
                catch
                {
                    // ignored
                }

                LogTraceFailureOnce(e.ToString());
            }
        }

        private static void LogTraceFailureOnce(string info)
        {
            if (loggedTraceFailure) return;
            loggedTraceFailure = true;

            string at = null;
            try
            {
                at = Native.MethodNameFromAddr(DeferredStackTracingImpl.lastFailureAddr, false);
            }
            catch
            {
                // ignored
            }

            Log.Error($"Multiplayer: deferred stack tracing failed (further failures aren't logged). At: {at ?? "unknown"}. {info}");
        }

        public static bool ShouldAddStackTraceForDesyncLog()
        {
            if (tracingFailed) return false;
            if (!SupportsDeferredStackTracing) return false;
            if (Multiplayer.Client == null) return false;
            if (Multiplayer.settings.desyncTracingMode == DesyncTracingMode.None) return false;
            if (Multiplayer.game == null) return false;

            if (!Multiplayer.game.gameComp.logDesyncTraces) return false;

            if (Rand.stateStack.Count > 1) return false;
            if (Multiplayer.IsReplay) return false;

            if (!Multiplayer.Ticking && !Multiplayer.ExecutingCmds) return false;

            // Worker thread timing isn't deterministic and LmfPtr belongs to the main thread
            if (!UnityData.IsInMainThread) return false;

            return ignoreTraces == 0;
        }
    }

    [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextID))]
    public static class UniqueIdsPatch
    {
        static void Postfix()
        {
            DeferredStackTracing.Postfix();
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    public static class ThingSpawnPatch
    {
        static void Postfix(Thing __instance)
        {
            if (__instance.def.HasThingIDNumber)
                DeferredStackTracing.Postfix();
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    public static class ThingDeSpawnPatch
    {
        static void Postfix(Thing __instance)
        {
            if (__instance.def.HasThingIDNumber)
                DeferredStackTracing.Postfix();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class EndCurrentJobPatch
    {
        static void Prefix(Pawn_JobTracker __instance)
        {
            if (MpVersion.IsDebug && __instance.curJob != null && DeferredStackTracing.ShouldAddStackTraceForDesyncLog())
                Multiplayer.game.sync.TryAddInfoForDesyncLog($"EndCurrentJob for {__instance.pawn}: {__instance.curJob}", "");
        }
    }

    [HarmonyPatch(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.WildAnimalSpawnerTick))]
    static class WildAnimalSpawnerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.WildPlantSpawnerTick))]
    static class WildPlantSpawnerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.SteadyEnvironmentEffectsTick))]
    static class SteadyEnvironmentEffectsTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(TempTerrainManager), nameof(TempTerrainManager.Tick))]
    static class TempTerrainManagerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }

    /*[HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellForWorker))]
    static class FindBestStorageCellTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }*/

    [HarmonyPatch(typeof(IntermittentSteamSprayer), nameof(IntermittentSteamSprayer.SteamSprayerTick))]
    static class SteamSprayerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }
}
