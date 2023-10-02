using System;
using Multiplayer.Client;
using Multiplayer.Client.Desyncs;

class Program
{
    public static unsafe void Main(string[] args)
    {
        Native.mini_parse_debug_option("disable_omit_fp");
        Native.InitLmfPtr(Native.NativeOS.Dummy);
        Native.EarlyInit(Native.NativeOS.Dummy);

        Test();
    }

    public static void Test()
    {
        int hash = 0;
        long[] trace = new long[32];
        DeferredStackTracingImpl.TraceImpl(trace, ref hash);

        foreach (long l in trace)
            Console.WriteLine(Native.MethodNameFromAddr(l, false));
    }
}
