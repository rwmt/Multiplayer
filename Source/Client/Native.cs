using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
//using Iced.Intel;
using UnityEngine;
using Verse;
//using Decoder = Iced.Intel.Decoder;

namespace Multiplayer.Client
{
    static class Native
    {
        public static IntPtr DomainPtr { get; private set; }

        // LMF is Last Managed Frame
        // current_thread->internal_thread->thread_info->tls[4]
        public static long LmfPtr { get; private set; }

        public static bool Linux => Application.platform == RuntimePlatform.LinuxEditor || Application.platform == RuntimePlatform.LinuxPlayer;
        public static bool Windows => Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer;
        public static bool OSX => Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer;

        public static void EarlyInit()
        {
            if (Linux)
                TheLinuxWay();
            if (OSX)
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

        public static void InitLmfPtr()
        {
            if (!UnityData.IsInMainThread)
                throw new Exception("Multiplayer.Client.Native data getter not running on the main thread!");

            // Don't bother on 32 bit runtimes
            if (IntPtr.Size == 4)
                return;

            var internalThreadField = AccessTools.Field(typeof(Thread), "internal_thread");
            var threadInfoField = AccessTools.Field(internalThreadField.FieldType, "runtime_thread_info");
            var threadInfoPtr = (long)(IntPtr)threadInfoField.GetValue(internalThreadField.GetValue(Thread.CurrentThread));

            // Struct offset found manually
            // Navigate by string: "Handle Stack"
            if (Linux)
                LmfPtr = threadInfoPtr + 0x480 - 8 * 4;
            else if (Windows)
                LmfPtr = threadInfoPtr + 0x448 - 8 * 4;
            else if (OSX)
                LmfPtr = threadInfoPtr + 0x418 - 8 * 4;
        }

        public static string MethodNameFromAddr(long addr, bool harmonyOriginals)
        {
            var domain = DomainPtr;
            var ji = mono_jit_info_table_find(domain, (IntPtr)addr);

            if (ji == IntPtr.Zero) return null;

            var ptrToPrint = mono_jit_info_get_method(ji);
            var codeStart = (long)mono_jit_info_get_code_start(ji);

            if (harmonyOriginals)
            {
                var original = MpUtil.GetOriginalFromHarmonyReplacement(codeStart);
                if (original != null)
                    ptrToPrint = original.MethodHandle.Value;
            }

            var name = mono_debug_print_stack_frame(ptrToPrint, -1, domain);

            return string.IsNullOrEmpty(name) ? null : name;
        }

        const string MonoWindows = "mono-2.0-bdwgc";
        const string MonoLinux = "libmonobdwgc-2.0.so";
        const string MonoOSX = "libmonobdwgc-2.0.dylib";

        [DllImport(MonoLinux, EntryPoint = "mono_dllmap_insert")]
        private static extern void mono_dllmap_insert_linux(IntPtr assembly, string dll, string func, string tdll, string tfunc);

        [DllImport(MonoOSX, EntryPoint = "mono_dllmap_insert")]
        private static extern void mono_dllmap_insert_osx(IntPtr assembly, string dll, string func, string tdll, string tfunc);

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
        public static extern IntPtr mono_domain_get();

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_compile_method(IntPtr method);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_class_from_mono_type(IntPtr type);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_class_vtable(IntPtr domain, IntPtr klass);

        [DllImport(MonoWindows)]
        public static extern string mono_method_get_reflection_name(IntPtr method);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_method_get_class(IntPtr method);

        [DllImport(MonoWindows)]
        public static extern IntPtr mono_class_get_image(IntPtr klass);

        public unsafe static bool CctorRan(Type t)
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
