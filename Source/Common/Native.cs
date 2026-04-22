using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;

namespace Multiplayer.Client
{
    public static class Native
    {
        public enum NativeOS
        {
            Windows, OSX, Linux, Dummy
        }

        public enum NativeArch
        {
            X64, ARM64
        }

        public static NativeArch CurrentArch =>
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? NativeArch.ARM64 : NativeArch.X64;

        public static IntPtr DomainPtr { get; private set; }

        // LMF is Last Managed Frame
        // current_thread->internal_thread->thread_info->tls[4]
        public static long LmfPtr { get; private set; }

        public static Func<long, MethodBase>? HarmonyOriginalGetter { get; set; }

        public static void EarlyInit(NativeOS os)
        {
            if (os == NativeOS.Linux)
                TheLinuxWay();
            if (os == NativeOS.OSX)
                TheOSXWay();

            EarlyInitInternal();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void TheLinuxWay() => mono_dllmap_insert_linux(IntPtr.Zero, MonoWindows, null, MonoLinux, null);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void TheOSXWay() => mono_dllmap_insert_osx(IntPtr.Zero, MonoWindows, null, MonoOSX, null);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EarlyInitInternal()
        {
            DomainPtr = mono_domain_get();
        }

        public static unsafe void InitLmfPtr(NativeOS os)
        {
            // Don't bother on 32 bit runtimes
            if (IntPtr.Size == 4)
                return;

            // ARM64 macOS doesn't use LMF - signal FP-only mode to DeferredStackTracingImpl
            if (CurrentArch == NativeArch.ARM64 && os == NativeOS.OSX)
            {
                LmfPtr = -1;
                return;
            }

            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic;

            var internalThreadField = typeof(Thread).GetField("internal_thread", all);
            var threadInfoField = internalThreadField.FieldType.GetField("runtime_thread_info", all);
            var threadInfoPtr = (long)(IntPtr)threadInfoField.GetValue(internalThreadField.GetValue(Thread.CurrentThread));

            // Struct offset found manually
            // Navigate by string: "Handle Stack"
            // Updated for RimWorld 1.6 (Unity 2022.3.35f1, Mono 6.13.0)
            if (os == NativeOS.Linux)
                LmfPtr = threadInfoPtr + 0x450; // Updated: 1.5 was 0x460, -16 bytes = 0x450 (following Windows pattern)
            else if (os == NativeOS.Windows)
                LmfPtr = threadInfoPtr + 0x418; // Updated for 1.6. Seems to work so far.
            else if (os == NativeOS.OSX)
                LmfPtr = threadInfoPtr + 0x3E8; // Updated: 1.5 was 0x3F8, -16 bytes = 0x3E8 (following Windows pattern)
            else if (os == NativeOS.Dummy)
            {
                LmfPtr = (long)Marshal.AllocHGlobal(3 * 8);
                *(long*)LmfPtr = LmfPtr;
                *(long*)(LmfPtr + 8) = 0;
            }
        }

        private static IntPtr MaybeFindHarmonyOriginalMethod(long addr, bool harmonyOriginals)
        {
            var ji = mono_jit_info_table_find(DomainPtr, (IntPtr)addr);
            if (ji == IntPtr.Zero) return IntPtr.Zero;

            var ptr = mono_jit_info_get_method(ji);
            var codeStart = (long)mono_jit_info_get_code_start(ji);

            if (harmonyOriginals && HarmonyOriginalGetter != null)
            {
                var original = HarmonyOriginalGetter(codeStart);
                if (original != null)
                    ptr = original.MethodHandle.Value;
            }

            return ptr;
        }

        public static string? MethodNameFromAddr(long addr, bool harmonyOriginals)
        {
            var ptr = MaybeFindHarmonyOriginalMethod(addr, harmonyOriginals);
            if (ptr == IntPtr.Zero) return null;
            var name = mono_debug_print_stack_frame(ptr, -1, DomainPtr);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        public static string? MethodNameNormalizedFromAddr(long addr, bool harmonyOriginals)
        {
            var ptr = MaybeFindHarmonyOriginalMethod(addr, harmonyOriginals);
            return ptr == IntPtr.Zero ? null : mono_method_get_reflection_name(ptr);
        }

        private static ConstructorInfo runtimeMethodHandleCtor = AccessTools.Constructor(typeof(RuntimeMethodHandle), [typeof(IntPtr)]);

        public static bool GetMethodAggressiveInlining(long addr)
        {
            var domain = DomainPtr;
            var ji = mono_jit_info_table_find(domain, (IntPtr)addr);

            if (ji == IntPtr.Zero) return false;

            var methodHandle = mono_jit_info_get_method(ji);
            var methodBase = GetMethodBaseFromRuntimePointer(methodHandle);

            if (methodBase == null) return false;

            return (methodBase.MethodImplementationFlags & MethodImplAttributes.AggressiveInlining) != 0;
        }

        public static MethodBase? GetMethodBaseFromRuntimePointer(IntPtr methodHandle)
        {
            if (methodHandle == IntPtr.Zero)
                return null;
            var rmh = (RuntimeMethodHandle)runtimeMethodHandleCtor.Invoke([methodHandle]);
            var rth = new RuntimeTypeHandle();
            var methodBase = MethodBase.GetMethodFromHandle(rmh, rth);
            return methodBase;
        }

        // const string MonoWindows = "mono-2.0-sgen.dll";
        const string MonoWindows = "mono-2.0-bdwgc";
        const string MonoLinux = "libmonobdwgc-2.0.so";
        const string MonoOSX = "libmonobdwgc-2.0.dylib";

        [DllImport(MonoLinux, EntryPoint = "mono_dllmap_insert")]
        private static extern void mono_dllmap_insert_linux(IntPtr assembly, string? dll, string? func, string? tdll, string? tfunc);

        [DllImport(MonoOSX, EntryPoint = "mono_dllmap_insert")]
        private static extern void mono_dllmap_insert_osx(IntPtr assembly, string? dll, string? func, string? tdll, string? tfunc);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_jit_info_table_find(IntPtr domain, IntPtr addr);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_jit_info_get_method(IntPtr ji);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_jit_info_get_code_start(IntPtr ji);

        [DllImport(MonoWindows)]
        public static extern int mono_jit_info_get_code_size(IntPtr ji);

        [DllImport(MonoWindows)]
        public static extern string mono_debug_print_stack_frame(IntPtr method, int nativeOffset, IntPtr domain);

        [DllImport(MonoWindows)]
        public static extern int mini_parse_debug_option(string option);

        [DllImport(MonoWindows)]
        public static extern void mono_set_defaults(int verboseLevel, int opts);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_domain_get();

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_compile_method(IntPtr method);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_class_from_mono_type(IntPtr type);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_class_vtable(IntPtr domain, IntPtr klass);

        [DllImport(MonoWindows)]
        public static extern int mono_method_get_token(IntPtr method);

        [DllImport(MonoWindows)]
        public static extern string mono_method_get_reflection_name(IntPtr method);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_method_get_class(IntPtr method);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_class_get_image(IntPtr klass);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_profiler_create(IntPtr profiler);

        public delegate void JitDoneCallback(IntPtr profiler, IntPtr method, IntPtr jinfo);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_profiler_set_jit_done_callback(IntPtr profiler, JitDoneCallback? cb);

        public static unsafe bool CctorRan(Type t)
        {
            return *((byte*)mono_class_vtable(mono_domain_get(), mono_class_from_mono_type(t.TypeHandle.Value)) + 45) != 0;
        }
    }

    /*public static class IcedDisasm
    {
        public unsafe static void DisasmMethod(long codeStart)
        {
            DisasmMethod(codeStart, Native.mono_jit_info_get_code_size(Native.mono_jit_info_table_find(Native.DomainPtr, (IntPtr)codeStart)));
        }

        public unsafe static void DisasmMethod(long codeStart, long codeSize)
        {
            //Log.Message($"code {Convert.ToString(*(uint*)codeStart, 16)}");
            //return;

            var codeRip = (ulong)codeStart;
            var codeReader = new UnsafeCodeReader() { start = (byte*)codeStart };
            var decoder = Decoder.Create(64, codeReader);
            decoder.IP = codeRip;
            ulong endRip = decoder.IP + (uint)codeSize + 8;

            var instructions = new InstructionList();
            while (decoder.IP < endRip)
                decoder.Decode(out instructions.AllocUninitializedElement());

            var formatter = new NasmFormatter();
            formatter.Options.DigitSeparator = "`";
            formatter.Options.FirstOperandCharIndex = 10;
            var output = new StringOutput();

            bool cond = (*(uint*)codeStart & 0xFFFFFF) != 0xEC8348 && *(byte*)codeStart != 0x55;

            Log.Message("Code size: " + codeSize, true);

            foreach (ref var instr in instructions)
            {
                var stringb = new StringBuilder();
                formatter.Format(instr, output);
                stringb.Append(instr.IP.ToString("X16"));
                stringb.Append(" ");
                int instrLen = instr.Length;
                int byteBaseIndex = (int)(instr.IP - codeRip);
                for (int i = 0; i < instrLen; i++)
                    stringb.Append(((byte*)codeStart)[byteBaseIndex + i].ToString("X2"));
                int missingBytes = 10 - instrLen;
                for (int i = 0; i < missingBytes; i++)
                    stringb.Append("  ");
                stringb.Append(" ");
                stringb.Append(output.ToStringAndReset());
                //if (cond)
                Log.Message(stringb.ToString(), ignoreStopLoggingLimit: true);
            }
        }

        public unsafe class UnsafeCodeReader : CodeReader
        {
            public byte* start;

            public unsafe override int ReadByte()
            {
                var val = *start;
                start++;
                return val;
            }
        }
    }*/
}
