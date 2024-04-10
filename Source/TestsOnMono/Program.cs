using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
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
    public static void Main2(string[] args)
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

    // Test patching generic methods with Harmony
    public static void Main(string[] args)
    {
        TestClassForPatches<SomeClass> test = new()
        {
            field = new SomeClassDerived { a = 2 }
        };

        Console.WriteLine(test.GetField().GetA());

        new Harmony("test").Patch(
            typeof(TestClassForPatches<SomeClass>).GetMethod("GetField"),
            postfix: new HarmonyMethod(typeof(Program).GetMethod("GenericPostfix"))
        );

        Console.WriteLine(test.GetField().GetA());
    }

    public static void GenericPostfix(ref object __result)
    {
        __result = new SomeClass2() { a = 3 };
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

    public class SomeClass
    {
        public int a;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GetA() => a;
    }

    public class SomeClassDerived : SomeClass
    {
    }

    public class SomeClass2
    {
        public int a;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GetA() => a;
    }

    class TestClassForPatches<T>
    {
        public T field;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public T GetField() => field;
    }
}
