using System;
using System.Reflection;
using Multiplayer.Common;

namespace Tests;

public class StaticFieldDumpTest
{
    /// <summary>
    /// Stands in for the platform-specific interop types MonoMod ships in a single assembly. Reading any
    /// static on the ones belonging to a different operating system runs an initializer that P/Invokes a
    /// library the host does not have. The shape is identical on every platform; only the type names
    /// differ, so this reproduces it without depending on which OS the tests run under.
    ///
    /// The explicit static constructor is deliberate: it suppresses beforefieldinit, so initialization
    /// happens exactly when the field is read rather than at some earlier point of the runtime's choosing.
    /// </summary>
    private static class FailsToInitialize
    {
        public static int Value;

        static FailsToInitialize() => throw new DllNotFoundException("libc");
    }

    private static class Ordinary
    {
        public static int Value;

        static Ordinary() => Value = 42;
    }

    private static FieldInfo StaticFieldOf(Type type, string name)
        => type.GetField(name, BindingFlags.Public | BindingFlags.Static);

    [Test]
    public void TryReadStaticValue_ReadsAnOrdinaryField()
    {
        var read = StaticFieldDump.TryReadStaticValue(
            StaticFieldOf(typeof(Ordinary), nameof(Ordinary.Value)),
            out var value,
            out var failure);

        Assert.That(read, Is.True);
        Assert.That(value, Is.EqualTo(42));
        Assert.That(failure, Is.Null);
    }

    [Test]
    public void TryReadStaticValue_ReportsAFailingInitializerInsteadOfPropagating()
    {
        var read = StaticFieldDump.TryReadStaticValue(
            StaticFieldOf(typeof(FailsToInitialize), nameof(FailsToInitialize.Value)),
            out _,
            out var failure);

        Assert.That(read, Is.False, "an unreadable field must be reported, not thrown");
        Assert.That(failure, Does.Contain(nameof(DllNotFoundException)),
            "the report should name the underlying cause, not the TypeInitializationException wrapping it");
    }
}
