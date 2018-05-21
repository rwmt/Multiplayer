using System;
using System.Runtime.InteropServices;

namespace Multiplayer.Client
{
    public static class SimpleProfiler
    {
        // Inits (or clears) the profiler
        [DllImport("simple_profiler.dll", CharSet = CharSet.Ansi)]
        private static extern void init_profiler(string id);

        // Starts collecting profiler data
        [DllImport("simple_profiler.dll")]
        private static extern void start_profiler();

        // Pauses data collection
        [DllImport("simple_profiler.dll")]
        private static extern void pause_profiler();

        // Prints collected data to file
        [DllImport("simple_profiler.dll")]
        private static extern void print_profiler(string filename);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public static readonly bool available;

        static SimpleProfiler()
        {
            available = GetModuleHandle("simple_profiler.dll").ToInt32() != 0;
            //available = false;
        }

        public static void Init(string id)
        {
            if (!available) return;
            init_profiler(id);
        }

        public static void Start()
        {
            if (!available) return;
            start_profiler();
        }

        public static void Pause()
        {
            if (!available) return;
            pause_profiler();
        }

        public static void Print(string file)
        {
            if (!available) return;
            print_profiler(file);
        }
    }
}