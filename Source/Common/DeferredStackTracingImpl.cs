using System;
using System.Runtime.CompilerServices;

namespace Multiplayer.Client.Desyncs;

public struct AddrTable()
{
    private const int StartingN = 10; // 1024
    private const int StartingShift = 64 - StartingN;
    private const int StartingSize = 1 << StartingN;
    private const float LoadFactor = 0.5f;

    private AddrInfo[] hashtable = new AddrInfo[StartingSize];

    public int Size => hashtable.Length;
    public int Entries { get; private set; } = 0;
    public int Collisions { get; private set; } = 0;

    private int shift = StartingShift;

    public ref AddrInfo GetOrCreateAddrInfo(long ret)
    {
        int indexmask = Size - 1;
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
        if (colls > Collisions) Collisions = colls;

        // When returning an unpopulated AddrInfo, assume it's going to get populated shortly and consider it used
        // immediately.
        if (info.addr == 0 && Entries++ > Size * LoadFactor) ResizeHashtable();
        return ref info;
    }

    private static ulong HashAddr(ulong addr) => ((addr >> 4) | addr << 60) * 11400714819323198485;

    private void ResizeHashtable()
    {
        var oldTable = hashtable;

        hashtable = new AddrInfo[Size * 2];
        shift--;
        Collisions = 0;

        int indexmask = Size - 1;

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
    }
}

public struct AddrInfo
{
    public long addr;
    public long stackUsage;
    public long nameHash;
}

public static class DeferredStackTracingImpl
{
    const long NotJit = long.MaxValue;
    const long RbpBased = long.MaxValue - 1;

    const long UsesRbpAsGpr = 1L << 50;
    const long UsesRbx = 1L << 51;
    const long RbpInfoClearMask = ~(UsesRbpAsGpr | UsesRbx);

    public const int HashInfluence = 6;

    public static AddrTable hashTable = new();

    public static unsafe int TraceImpl(long[] traceIn, ref int hash, int skipFrames = 0)
    {
        if (Native.LmfPtr == 0)
            return 0;

        long rbp = GetRbp();

        long stck = rbp;
        rbp = *(long*)rbp;

        long lmfPtr = *(long*)Native.LmfPtr;

        int depth = 0; // frames seen
        int index = 0; // frames returned through long[] traceIn
        while (true)
        {
            var ret = *(long*)(stck + 8);
            ref var info = ref hashTable.GetOrCreateAddrInfo(ret);
            if (info.addr == 0) UpdateNewElement(ref info, ret);

            long stackUsage = info.stackUsage;
            if (stackUsage == NotJit)
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

            if (depth >= skipFrames)
            {
                traceIn[index] = ret;

                // info.nameHash == 0 marks methods to skip
                if (index < HashInfluence && info.nameHash != 0) hash = HashCombineInt(hash, (int)info.nameHash);

                index++;
            }

            if (info.nameHash != 0 && ++depth == traceIn.Length)
                break;

            if (stackUsage == RbpBased)
            {
                stck = rbp;
                rbp = *(long*)rbp;
                continue;
            }

            stck += 8;

            if ((stackUsage & UsesRbpAsGpr) != 0)
            {
                if ((stackUsage & UsesRbx) != 0)
                    rbp = *(long*)(stck + 16);
                else
                    rbp = *(long*)(stck + 8);

                stackUsage &= RbpInfoClearMask;
            }

            stck += stackUsage;
        }

        return index;
    }


    private static void UpdateNewElement(ref AddrInfo info, long ret)
    {
        info.addr = ret;
        info.stackUsage = GetStackUsage(ret);

        var normalizedMethodNameBetweenOS = Native.MethodNameNormalizedFromAddr(ret, true);

        info.nameHash =
            normalizedMethodNameBetweenOS == null ? 1 :
            Native.GetMethodAggressiveInlining(ret) ? 0 :
            StableStringHash(normalizedMethodNameBetweenOS);
    }

    static unsafe long GetStackUsage(long addr)
    {
        var ji = Native.mono_jit_info_table_find(Native.DomainPtr, (IntPtr)addr);

        if (ji == IntPtr.Zero)
            return NotJit;

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
            return RbpBased;

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
            stackUsage |= UsesRbpAsGpr;
        }
        else if (*at == 0x241C8948 && *(at + 1) == 0x246C8948)
        {
            stackUsage |= UsesRbpAsGpr;
            stackUsage |= UsesRbx;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe long GetRbp()
    {
        long rbp = 0;

        return *(&rbp + 1);
    }

    private static int HashCombineInt(int seed, int value) =>
        (int)(seed ^ (value + 2654435769u + (seed << 6) + (seed >> 2)));

    private static int StableStringHash(string? str)
    {
        if (str == null)
        {
            return 0;
        }
        int num = 23;
        int length = str.Length;
        for (int i = 0; i < length; i++)
        {
            num = num * 31 + str[i];
        }
        return num;
    }
}
