using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Multiplayer.Common;

namespace Multiplayer.Client.Desyncs;

public struct AddrTable() : IEnumerable<AddrInfo>
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

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<AddrInfo> GetEnumerator()
    {
        // AsEnumerable needed to get a generic IEnumerable<AddrInfo>
        using var enumerator = hashtable.AsEnumerable().GetEnumerator();
        while (enumerator.MoveNext())
        {
            var addr = enumerator.Current;
            if (addr.addr == 0) continue;
            yield return addr;
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

    const long Arm64FpBased = long.MaxValue - 2;

    public const int HashInfluence = 6;

    public static AddrTable hashTable = new();

    private static bool IsArm64 => Native.CurrentArch == Native.NativeArch.ARM64;

    public static unsafe int TraceImpl(long[] traceIn, ref int hash, int skipFrames = 0)
    {
        if (Native.LmfPtr == 0)
            return 0;

        if (IsArm64)
            return TraceImplArm64(traceIn, ref hash, skipFrames);

        return TraceImplX64(traceIn, ref hash, skipFrames);
    }

    // ARM64 ABI always uses frame pointers (X29), making this simpler than x64.
    // Frame layout: [FP] = previous FP (saved X29), [FP+8] = return address (saved X30/LR)
    private static unsafe int TraceImplArm64(long[] traceIn, ref int hash, int skipFrames)
    {
        if (Native.LmfPtr != -1)
            return 0;

        long fp = GetFp();

        int depth = 0;
        int index = 0;

        while (fp != 0 && depth < traceIn.Length + skipFrames + 100)
        {
            long ret = *(long*)(fp + 8);
            if (ret == 0 || ret < 0x1000)
                break;

            ref var info = ref hashTable.GetOrCreateAddrInfo(ret);
            if (info.addr == 0) UpdateNewElementArm64(ref info, ret);

            // Stop at unmanaged frames - no LMF chain walking on ARM64
            if (info.stackUsage == NotJit)
                break;

            if (depth >= skipFrames)
            {
                traceIn[index] = ret;

                if (index < HashInfluence && info.nameHash != 0)
                    hash = HashCombineInt(hash, (int)info.nameHash);

                index++;
            }

            if (info.nameHash != 0 && ++depth == traceIn.Length)
                break;

            long prevFp = *(long*)fp;
            if (prevFp == fp)
                break;

            fp = prevFp;
        }

        return index;
    }

    private static unsafe int TraceImplX64(long[] traceIn, ref int hash, int skipFrames)
    {
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

            // traceIn length limits above all how many frames are visited, not how many are populated.
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

    private static void UpdateNewElementArm64(ref AddrInfo info, long ret)
    {
        info.addr = ret;

        var ji = Native.mono_jit_info_table_find(Native.DomainPtr, (IntPtr)ret);
        if (ji == IntPtr.Zero)
        {
            info.stackUsage = NotJit;
            info.nameHash = 1;
            return;
        }

        // No prologue parsing needed - ARM64 always uses FP-based frames
        info.stackUsage = Arm64FpBased;

        var normalizedMethodNameBetweenOS = Native.MethodNameNormalizedFromAddr(ret, true);

        info.nameHash =
            normalizedMethodNameBetweenOS == null ? 1 :
            Native.GetMethodAggressiveInlining(ret) ? 0 :
            StableStringHash(normalizedMethodNameBetweenOS);
    }

    private static unsafe long GetStackUsage(long addr)
    {
        var ji = Native.mono_jit_info_table_find(Native.DomainPtr, (IntPtr)addr);

        if (ji == IntPtr.Zero)
            return NotJit;

        var start = (uint*)Native.mono_jit_info_get_code_start(ji);
        long usage = 0;

        // Emitted at: https://github.com/Unity-Technologies/mono/blob/2022.3.35f1/mono/mini/mini-amd64.c#L7652
        // - diverges into: https://github.com/Unity-Technologies/mono/blob/2022.3.35f1/mono/arch/amd64/amd64-codegen.h#L190-L193
        if ((*start & 0xFFFFFF) == 0xEC8348) // sub rsp,XX (4883EC XX)
        {
            usage = *start >> 24;
            start += 1;
        }
        // - diverges into: https://github.com/Unity-Technologies/mono/blob/2022.3.35f1/mono/arch/amd64/amd64-codegen.h#L199-L202
        //   basically just a long form of the above branch.
        else if ((*start & 0xFFFFFF) == 0xEC8148) // sub rsp,XXXXXXXX (4881EC XXXXXXXX)
        {
            usage = *(uint*)((long)start + 3);
            start = (uint*)((long)start + 7);
        }

        if (usage != 0)
        {
            CheckRbpUsage(start, ref usage);
            return usage;
        }

        // https://github.com/Unity-Technologies/mono/blob/2022.3.35f1/mono/mini/mini-amd64.c#L7559
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
        // (The callee saved registers are always in the same order
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

    static DeferredStackTracingImpl()
    {
        // All of this code assumes that the frame pointer offset stays the same throughout the method invocations.
        // Mono is generally allowed to recompile code, which could cause issues for us, but GetRbp/GetFp are annotated
        // as NoInline and NoOptimization to heavily discourage any changes and avoid breaking.

        if (Native.CurrentArch == Native.NativeArch.ARM64)
        {
            InitArm64Offset();
        }
        else
        {
            InitX64Offset();
        }
    }

    private static unsafe void InitX64Offset()
    {
        var method = AccessTools.DeclaredMethod(typeof(DeferredStackTracingImpl), nameof(GetRbp))!;
        var rbpCodeStart = Native.mono_compile_method(method.MethodHandle.Value);
        // Add 1 byte because Mono recognizes the jit_info to be just after the code start address returned by
        // the compile method.
        var jitInfo = Native.mono_jit_info_table_find(Native.DomainPtr, rbpCodeStart + 1);
        var instStart = Native.mono_jit_info_get_code_start(jitInfo);
        var instLen = Native.mono_jit_info_get_code_size(jitInfo);
        // Search for the following instruction:
        // mov rax, imm<MagicNumber> (48b8 <MagicNumber>)
        // It should directly precede:
        // mov [rbp-XX], rax
        // From which we can extract the offset from rbp.
        byte[] magicBytes = [0x48, 0xb8, ..BitConverter.GetBytes(MagicNumber)];

        // Make sure we don't access out-of-bounds memory.
        // magicBytes.Length -- mov rax, <MagicNumber>
        // sizeof(uint)      -- mov [rbp-XX], rax
        var maxLen = instLen - magicBytes.Length - sizeof(uint);
        for (int i = 0; i < maxLen; i++)
        {
            byte* at = (byte*)instStart + i;
            var matches = ByteSpanEqual(at, magicBytes.Length, magicBytes);
            if (!matches) continue;

            uint* match = (uint*)(at + magicBytes.Length);
            // mov [rbp-XX], rax    (488945XX)
            if ((*match & 0xFFFFFF) == 0x458948)
            {
                offsetFromRbp = (sbyte)(*match >> 24);
                return;
            }
        }

        // To analyze the assembly dump, remove the offset prefixes at the start of each line and paste the hex to
        // a site like https://defuse.ca/online-x86-assembler.htm#disassembly2. Choose x64. Search for the
        // magic number and compare the code with the loop above.
        var asm = HexDump((byte*)instStart, instLen);
        ServerLog.Error(
            $"Unexpected GetRbp asm structure. Couldn't find a magic bytes match. " +
            $"Using fallback offset ({offsetFromRbp}). " +
            $"Asm dump for the method: \n{asm}");
    }

    private static unsafe void InitArm64Offset()
    {
        var method = AccessTools.DeclaredMethod(typeof(DeferredStackTracingImpl), nameof(GetFp))!;
        var fpCodeStart = Native.mono_compile_method(method.MethodHandle.Value);
        var jitInfo = Native.mono_jit_info_table_find(Native.DomainPtr, fpCodeStart + 1);
        var instStart = Native.mono_jit_info_get_code_start(jitInfo);
        var instLen = Native.mono_jit_info_get_code_size(jitInfo);

        // On ARM64, we search for the first STUR or STR instruction that stores to [X29 + offset].
        // Under current Mono codegen for this small GetFp() body, the prologue (STP X29/X30,
        // MOV X29, SP) uses X29 as a source, so the first store-to-[X29] is the MagicNumber
        // assignment.
        // ARM64 instructions are always 4 bytes, aligned.
        //
        // STUR Xt, [Xn, #simm9] - Store (unscaled) for small negative offsets
        // Encoding: 1111 1000 000i iiii iiii 00nn nnnt tttt
        // Where: simm9 is signed 9-bit immediate, Rn=base reg, Rt=source reg
        // For X29 as base: Rn = 11101 (29)
        //
        // STR Xt, [Xn, #uimm12] - Store (unsigned scaled) for larger positive offsets
        // Encoding: 1111 1001 00ii iiii iiii iinn nnnt tttt
        // Where: uimm12 is unsigned 12-bit immediate (scaled by 8), Rn=base reg, Rt=source reg

        uint* instructions = (uint*)instStart;
        int numInstructions = instLen / 4;

        for (int i = 0; i < numInstructions; i++)
        {
            uint inst = instructions[i];

            // STUR Xt, [X29, #simm9] where Rn=29 (11101) at bits 9-5
            if ((inst & 0xFFE00C00) == 0xF8000000) // STUR family
            {
                uint rn = (inst >> 5) & 0x1F;
                if (rn == 29) // X29 is base register
                {
                    int simm9 = (int)((inst >> 12) & 0x1FF);
                    if ((simm9 & 0x100) != 0) // Sign-extend
                        simm9 |= unchecked((int)0xFFFFFE00);

                    offsetFromFp = simm9;
                    return;
                }
            }

            // STR Xt, [X29, #uimm12] (scaled by 8)
            if ((inst & 0xFFC00000) == 0xF9000000) // STR (immediate, unsigned offset)
            {
                uint rn = (inst >> 5) & 0x1F;
                if (rn == 29) // X29 is base register
                {
                    uint uimm12 = ((inst >> 10) & 0xFFF) * 8;
                    offsetFromFp = (int)uimm12;
                    return;
                }
            }
        }

        var asm = HexDump((byte*)instStart, instLen);
        ServerLog.Error(
            $"ARM64: Couldn't find store instruction with X29 base in GetFp. " +
            $"Using fallback offset ({offsetFromFp}). " +
            $"This may cause incorrect stack traces. Assembly dump:\n{asm}");
    }

    private static unsafe bool ByteSpanEqual(byte* start, int len, byte[] arr)
    {
        for (var i = 0; i < len; i++)
            if (*(start + i) != arr[i])
                return false;
        return true;
    }

    private static unsafe string HexDump(byte* start, int len, int bytesPerLine = 16)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < len; i += bytesPerLine)
        {
            sb.Append($"{i:X4}: ");

            for (int j = 0; j < bytesPerLine && i + j < len; j++) sb.Append($"{start[i + j]:X2} ");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // Magic number is used to locate the relevant method code.
    private const long MagicNumber = 0x0123456789ABCDEF;

    private static sbyte offsetFromRbp = -8;
    private static int offsetFromFp = -8;

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static unsafe long GetRbp()
    {
        // This variable declaration compiles down to the following IL:
        // ldc.i8 <MagicNumber>
        // stloc.0
        // In turn, the second IL instruction compiles to amd64 as:
        // mov qword ptr [rbp-XX], rax
        // From which we can extract the offset (XX) to reliably calculate the rbp address
        long register = MagicNumber;

        return *(long*)((byte*)&register - offsetFromRbp);
    }

    // Same principle as GetRbp but for ARM64 X29 (frame pointer).
    // The magic number store compiles to: STUR Xn, [X29, #-offset] or STR Xn, [X29, #offset]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static unsafe long GetFp()
    {
        long register = MagicNumber;
        return *(long*)((byte*)&register - offsetFromFp);
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
