using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Multiplayer.Client
{
    delegate bool walk_stack(IntPtr methodHandle, int native, int il, bool managed, IntPtr data);

    static class Native
    {
        [DllImport("mono.dll")]
        static extern bool mono_debug_using_mono_debugger();

        [DllImport("mono.dll")]
        static extern IntPtr mono_debug_print_stack_frame(IntPtr methodHandle, int codePtr, IntPtr domainHandle);

        [DllImport("mono.dll")]
        public static extern IntPtr mono_domain_get();

        [DllImport("mono.dll")]
        static extern IntPtr mono_debug_init(int format);

        [DllImport("mono.dll")]
        public static extern void mono_set_defaults(IntPtr verboseLevel, int opts);

        [DllImport("mono.dll")]
        static extern IntPtr mono_debug_open_image_from_memory(IntPtr imageHandle, IntPtr contents, IntPtr size);

        [DllImport("mono.dll")]
        static extern IntPtr mono_debug_find_method(IntPtr methodHandle, IntPtr domainHandle);

        [DllImport("mono.dll")]
        static extern IntPtr mono_class_get_image(IntPtr classHandle);

        [DllImport("mono.dll")]
        static extern IntPtr mono_type_get_class(IntPtr typeHandle);

        [DllImport("mono.dll")]
        public static extern IntPtr mono_valloc(IntPtr addr, IntPtr length, IntPtr flags);

        [DllImport("mono.dll")]
        public static extern IntPtr mono_method_get_header(IntPtr methodHandle);

        [DllImport("mono.dll")]
        public static unsafe extern int mono_method_get_flags(IntPtr methodHandle, int* iflags);

        [DllImport("mono.dll")]
        public static extern IntPtr mono_method_header_get_code(IntPtr header, IntPtr codeSize, IntPtr maxStack);

        [DllImport("mono.dll")]
        public static extern void mono_stack_walk(IntPtr walkFunc, IntPtr data);
    }
}
