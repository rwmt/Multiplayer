using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.Client.Patches;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Desyncs
{
    [EarlyPatch]
    [HarmonyPatch]

    static class DeferredStackTracing
    {
        public static int ignoreTraces;
        public static long maxTraceDepth;
        public static int randCalls;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Value));
            yield return AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Int));
            //yield return AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn));
        }

        public static int acc;

        public static void Postfix()
        {
            if (Native.LmfPtr == 0) return;
            if (!ShouldAddStackTraceForDesyncLog()) return;

            var logItem = SimplePool<StackTraceLogItemRaw>.Get();
            var trace = logItem.raw;
            int hash = 0;
            int depth = DeferredStackTracingImpl.TraceImpl(trace, ref hash);

            Multiplayer.game.sync.TryAddStackTraceForDesyncLogRaw(logItem, depth, hash);

            acc = Gen.HashCombineInt(acc, depth, hash, (int)Rand.iterations);
        }

        public static bool ShouldAddStackTraceForDesyncLog()
        {
            if (Multiplayer.Client == null) return false;
            if (Multiplayer.settings.desyncTracingMode == DesyncTracingMode.None) return false;
            if (Multiplayer.game == null) return false;

            // Only log if debugging enabled in Host Server menu
            if (!Multiplayer.game.gameComp.logDesyncTraces) return false;

            if (Rand.stateStack.Count > 1) return false;
            if (Multiplayer.IsReplay) return false;

            if (!Multiplayer.Ticking && !Multiplayer.ExecutingCmds) return false;
            if (AsyncTimeComp.tickingMap is not { uniqueID: 0 }) return false;

            return ignoreTraces == 0;
        }
    }

    static class DeferredStackTracingImpl
    {
        struct AddrInfo
        {
            public long addr;
            public long stackUsage;
            public long nameHash;
            public long unused;
        }

        const int StartingN = 7;
        const int StartingShift = 64 - StartingN;
        const int StartingSize = 1 << StartingN;
        const float LoadFactor = 0.5f;

        static AddrInfo[] hashtable = new AddrInfo[StartingSize];
        public static int hashtableSize = StartingSize;
        public static int hashtableEntries;
        public static int hashtableShift = StartingShift;
        public static int collisions;

        const long NotJIT = long.MaxValue;
        const long RBPBased = long.MaxValue - 1;

        const long UsesRBPAsGPR = 1 << 50;
        const long UsesRBX = 1 << 51;
        const long RBPInfoClearMask = ~(UsesRBPAsGPR | UsesRBX);

        public const int MaxDepth = 32;
        public const int HashInfluence = 6;

        public unsafe static int TraceImpl(long[] traceIn, ref int hash)
        {
            long[] trace = traceIn;
            long rbp = GetRbp();
            long stck = rbp;
            rbp = *(long*)rbp;

            int indexmask = hashtableSize - 1;
            int shift = hashtableShift;

            long ret;
            long lmfPtr = *(long*)Native.LmfPtr;

            int depth = 0;

            while (true)
            {
                ret = *(long*)(stck + 8);

                int index = (int)(HashAddr((ulong)ret) >> shift);
                ref var info = ref hashtable[index];
                int colls = 0;

                // Open addressing
                while (info.addr != 0 && info.addr != ret)
                {
                    index = (index + 1) & indexmask;
                    info = ref hashtable[index];
                    colls++;
                }

                if (colls > collisions)
                    collisions = colls;

                long stackUsage = 0;

                if (info.addr != 0)
                    stackUsage = info.stackUsage;
                else
                    stackUsage = UpdateNewElement(ref info, ret);

                if (stackUsage == NotJIT)
                {
                    // LMF (Last Managed Frame) layout on x64:
                    // previous
                    // rbp
                    // rsp

                    lmfPtr = *(long*)lmfPtr;
                    var lmfRbp = *(long*)(lmfPtr + 8);

                    if (lmfPtr == 0 || lmfRbp == 0)
                        break;

                    rbp = lmfRbp;
                    stck = *(long*)(lmfPtr + 16) - 16;

                    continue;
                }

                trace[depth] = ret;

                if (depth < HashInfluence)
                    hash = Gen.HashCombineInt(hash, (int)info.nameHash);

                if (++depth == MaxDepth)
                    break;

                if (stackUsage == RBPBased)
                {
                    stck = rbp;
                    rbp = *(long*)rbp;
                    continue;
                }

                stck += 8;

                if ((stackUsage & UsesRBPAsGPR) != 0)
                {
                    if ((stackUsage & UsesRBX) != 0)
                        rbp = *(long*)(stck + 16);
                    else
                        rbp = *(long*)(stck + 8);

                    stackUsage &= RBPInfoClearMask;
                }

                stck += stackUsage;
            }

            return depth;
        }

        static long UpdateNewElement(ref AddrInfo info, long ret)
        {
            long stackUsage = GetStackUsage(ret);

            info.addr = ret;
            info.stackUsage = stackUsage;

            var rawName = Native.MethodNameFromAddr(ret, true);
            info.nameHash = rawName != null ? GenText.StableStringHash(SyncCoordinator.MethodNameWithoutIL(rawName)) : 1;

            hashtableEntries++;
            if (hashtableEntries > hashtableSize * LoadFactor)
                ResizeHashtable();

            return stackUsage;
        }

        static ulong HashAddr(ulong addr) => ((addr >> 4) | addr << 60) * 11400714819323198485;

        static int ResizeHashtable()
        {
            var oldTable = hashtable;

            hashtableSize *= 2;
            hashtableShift--;

            hashtable = new AddrInfo[hashtableSize];
            collisions = 0;

            int indexmask = hashtableSize - 1;
            int shift = hashtableShift;

            for (int i = 0; i < oldTable.Length; i++)
            {
                ref var oldInfo = ref oldTable[i];
                if (oldInfo.addr != 0)
                {
                    int index = (int)(HashAddr((ulong)oldInfo.addr) >> shift);

                    while (hashtable[index].addr != 0)
                        index = (index + 1) & indexmask;

                    ref var newInfo = ref hashtable[index];
                    newInfo.addr = oldInfo.addr;
                    newInfo.stackUsage = oldInfo.stackUsage;
                    newInfo.nameHash = oldInfo.nameHash;
                }
            }

            return indexmask;
        }

        unsafe static long GetStackUsage(long addr)
        {
            var ji = Native.mono_jit_info_table_find(Native.DomainPtr, (IntPtr)addr);

            if (ji == IntPtr.Zero)
                return NotJIT;

            var start = (uint*)Native.mono_jit_info_get_code_start(ji);
            long usage = 0;

            if ((*start & 0xFFFFFF) == 0xEC8348) // sub rsp,XX (4883EC XX)
            {
                usage = *start >> 24;
                start += 1;
            } else if ((*start & 0xFFFFFF) == 0xEC8148) // sub rsp,XXXXXXXX (4881EC XXXXXXXX)
            {
                usage = *(uint*)((long)start + 3);
                start = (uint*)((long)start + 7);
            }

            if (usage != 0)
            {
                CheckRbpUsage(start, ref usage);
                return usage;
            }

            // push rbp (55)
            if (*(byte*)start == 0x55)
                return RBPBased;

            throw new Exception($"Deferred stack tracing: Unknown function header {*start} {Native.MethodNameFromAddr(addr, false)}");
        }

        private static unsafe void CheckRbpUsage(uint* at, ref long stackUsage)
        {
            // If rbp is used as a gp reg then the prologue looks like (after frame alloc):
            // mov [rsp],rbp   (48892C24)
            // or:
            // mov [rsp],rbx   (48891C24)
            // mov [rsp+8],rbp (48896C2408)
            // (The calle saved registers are always in the same order
            // and are saved at the bottom of the frame)

            if (*at == 0x242C8948)
            {
                stackUsage |= UsesRBPAsGPR;
            }
            else if (*at == 0x241C8948 && *(at + 1) == 0x246C8948)
            {
                stackUsage |= UsesRBPAsGPR;
                stackUsage |= UsesRBX;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static unsafe long GetRbp()
        {
            long rbp = 0;
            return *(&rbp + 1);
        }
    }

    [HarmonyPatch(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.WildAnimalSpawnerTick))]
    static class WildAnimalSpawnerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Postfix() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.WildPlantSpawnerTick))]
    static class WildPlantSpawnerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Postfix() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.SteadyEnvironmentEffectsTick))]
    static class SteadyEnvironmentEffectsTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Postfix() => DeferredStackTracing.ignoreTraces--;
    }

    /*[HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellForWorker))]
    static class FindBestStorageCellTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Postfix() => DeferredStackTracing.ignoreTraces--;
    }*/

    [HarmonyPatch(typeof(IntermittentSteamSprayer), nameof(IntermittentSteamSprayer.SteamSprayerTick))]
    static class SteamSprayerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Postfix() => DeferredStackTracing.ignoreTraces--;
    }
}
