using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Multiplayer.Client
{
    static class Native
    {
        [DllImport("mono.dll")]
        static extern bool mono_debug_using_mono_debugger();

        [DllImport("mono.dll")]
        static extern IntPtr mono_debug_print_stack_frame(IntPtr methodHandle, int codePtr, IntPtr domainHandle);

        [DllImport("mono.dll")]
        static extern IntPtr mono_domain_get();

        [DllImport("mono.dll")]
        static extern IntPtr mono_debug_init(int format);

        [DllImport("mono.dll")]
        static extern IntPtr mono_debug_open_image_from_memory(IntPtr imageHandle, IntPtr contents, IntPtr size);

        [DllImport("mono.dll")]
        static extern IntPtr mono_debug_find_method(IntPtr methodHandle, IntPtr domainHandle);

        [DllImport("mono.dll")]
        static extern IntPtr mono_class_get_image(IntPtr classHandle);

        [DllImport("mono.dll")]
        static extern IntPtr mono_type_get_class(IntPtr typeHandle);
    }
}
