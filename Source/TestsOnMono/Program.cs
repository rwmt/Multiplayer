using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.Desyncs;

class Program
{
    public static void Main(string[] args)
    {
        Native.mini_parse_debug_option("disable_omit_fp");
        Native.InitLmfPtr(Native.NativeOS.Dummy);
        Native.EarlyInit(Native.NativeOS.Dummy);

        TestClass<int>.Test1<int>();
        TestClass<int>.Test();
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
