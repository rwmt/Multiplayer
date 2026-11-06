using System.Reflection;
using System.Runtime.InteropServices;
using Multiplayer.Common;

namespace Tests;

public class InstrumentationTargetsTest
{
    private static class Candidates
    {
        public static void Ordinary()
        {
        }

        /// <summary>
        /// Stands in for ArbiterWindowFix.SetParent, the declaration that aborts the real action.
        ///
        /// Declared but never called, so it needs no library at run time and behaves the same on every
        /// platform. What matters is only that reflection reports it as extern.
        /// </summary>
        [DllImport("User32")]
        public static extern int SetParent(int hwnd, int nCmdShow);

        public static void Generic<T>()
        {
        }

        public static int Value => 0;
    }

    private static MethodBase MethodOf(string name)
        => typeof(Candidates).GetMethod(name, BindingFlags.Public | BindingFlags.Static);

    [Test]
    public void ShouldInstrument_AnOrdinaryMethod()
    {
        Assert.That(InstrumentationTargets.ShouldInstrument(MethodOf(nameof(Candidates.Ordinary))), Is.True);
    }

    [Test]
    public void ShouldNotInstrument_AnExternMethod()
    {
        var method = MethodOf(nameof(Candidates.SetParent));

        Assert.That(method.Attributes.HasFlag(MethodAttributes.PinvokeImpl), Is.True,
            "the stand-in must actually be extern for this test to mean anything");
        Assert.That(InstrumentationTargets.ShouldInstrument(method), Is.False,
            "an extern method has no IL body, so Harmony emits a malformed wrapper and the runtime rejects it");
    }

    [Test]
    public void ShouldNotInstrument_AGenericMethod()
    {
        Assert.That(InstrumentationTargets.ShouldInstrument(MethodOf(nameof(Candidates.Generic))), Is.False);
    }

    [Test]
    public void ShouldNotInstrument_APropertyGetter()
    {
        var getter = typeof(Candidates).GetProperty(nameof(Candidates.Value), BindingFlags.Public | BindingFlags.Static)!.GetGetMethod();

        Assert.That(InstrumentationTargets.ShouldInstrument(getter), Is.False);
    }

    [Test]
    public void ShouldNotInstrument_TheLoggerItself()
    {
        var logger = typeof(SelfReference).GetMethod(
            InstrumentationTargets.LoggerMethodName,
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(InstrumentationTargets.ShouldInstrument(logger), Is.False,
            "instrumenting the logger would make every logged call log itself");
    }

    private static class SelfReference
    {
        // Named to match the real prefix, so the guard is exercised by name as it is in production.
        public static void MultiplayerMethodCallLogger()
        {
        }
    }
}
