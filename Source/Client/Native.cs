using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using HarmonyLib;
using Iced.Intel;
using UnityEngine;
using Verse;
using Decoder = Iced.Intel.Decoder;

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
            if (!Windows)
                mono_dllmap_insert(IntPtr.Zero, MonoWindows, null, MonoNonWindows, null);

            EarlyInitInternal();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EarlyInitInternal()
        {
            DomainPtr = mono_domain_get();
        }

        // Always run this on the main thread
        public static void InitLmfPtr()
        {
            var internalThreadField = AccessTools.Field(typeof(Thread), "internal_thread");
            var threadInfoField = AccessTools.Field(internalThreadField.FieldType, "runtime_thread_info");
            var threadInfoPtr = (long)(IntPtr)threadInfoField.GetValue(internalThreadField.GetValue(Thread.CurrentThread));

            // Struct offset found manually
            // Navigate by "Handle Stack" string
            if (Linux)
                LmfPtr = threadInfoPtr + 0x480 - 8 * 4;
            else if (Windows)
                LmfPtr = threadInfoPtr + 0x448 - 8 * 4;
            else if (OSX)
                LmfPtr = threadInfoPtr + 0x448 - 8 * 4;
        }

        public static string MethodNameFromAddr(long addr)
        {
            var domain = DomainPtr;
            var ji = mono_jit_info_table_find(domain, (IntPtr)addr);

            if (ji == IntPtr.Zero) return null;

            var methodPtr = mono_jit_info_get_method(ji);
            var codeStart = mono_jit_info_get_code_start(ji);
            var codeSize = mono_jit_info_get_code_size(ji);
            var name = mono_debug_print_stack_frame(methodPtr, (int)(addr - (long)codeStart), domain);
            if (name == null || name.Length == 0) return null;

            return name;
        }

        const string MonoWindows = "mono-2.0-bdwgc";
        const string MonoNonWindows = "libmonobdwgc-2.0.so";

        [DllImport(MonoNonWindows)]
        private static extern void mono_dllmap_insert(IntPtr assembly, string dll, string func, string tdll, string tfunc);

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

        public unsafe static bool CctorRan(Type t)
        {
            return *((byte*)mono_class_vtable(mono_domain_get(), mono_class_from_mono_type(t.TypeHandle.Value)) + 45) != 0;
        }
    }

    public static class IcedDisasm
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
    }
}
