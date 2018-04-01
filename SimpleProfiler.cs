using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Verse;

namespace Multiplayer
{
    public static class SimpleProfiler
    {
        // Inits (or clears) the profiler
        [DllImport("simple_profiler.dll")]
        private static extern void init_profiler();

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
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private static bool available;

        static SimpleProfiler()
        {
            available = GetModuleHandle("simple_profiler.dll").ToInt32() != 0;
        }

        public static void Init()
        {
            if (!available) return;
            init_profiler();
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