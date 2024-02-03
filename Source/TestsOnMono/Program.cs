using System;
using System.Runtime.CompilerServices;
using Multiplayer.Client;
using Multiplayer.Client.Desyncs;
using Multiplayer.Common;

// These only work on Windows Mono
namespace TestsOnMono;

static class Program
{
    // Test DeferredStackTracing
    public static void Main1(string[] args)
    {
        Native.mini_parse_debug_option("disable_omit_fp");
        Native.InitLmfPtr(Native.NativeOS.Dummy);
        Native.EarlyInit(Native.NativeOS.Dummy);

        TestClass<int>.Test1<int>();
        TestClass<int>.Test();
    }

    // Test rounding modes
    public static void Main(string[] args)
    {
        void Print()
        {
            Console.WriteLine(ExternMethods.GetRound());
            Console.WriteLine(RoundMode.GetCurrentRoundMode());
        }

        Print();
        ExternMethods.SetRound(RoundModeEnum.Upward);
        Print();
        ExternMethods.SetRound(RoundModeEnum.Downward);
        Print();
        ExternMethods.SetRound(RoundModeEnum.TowardZero);
        Print();
    }

    class TestClass<S>
    {
        public static void Test()
        {
            int hash = 0;
            long[] trace = new long[8];
            DeferredStackTracingImpl.TraceImpl(trace, ref hash);
            Console.WriteLine(hash);

            foreach (long l in trace)
                Console.WriteLine(Native.MethodNameFromAddr(l, false));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)]
        public static void Test1<T>()
        {
            Test();
        }
    }
}
