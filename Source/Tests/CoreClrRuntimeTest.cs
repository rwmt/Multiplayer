using Multiplayer.Client;

namespace Tests;

public class CoreClrRuntimeTest
{
    [Test]
    public void MonoInitializationIsSkippedOnCoreClr()
    {
        Assert.That(Native.IsMonoRuntime, Is.False);
        // Both calls used to load Mono libraries or read Mono thread memory.
        Native.EarlyInit(Native.NativeOS.Windows);
        Native.InitLmfPtr(Native.NativeOS.Windows);
        Assert.That(Native.DomainPtr, Is.EqualTo(IntPtr.Zero));
        Assert.That(Native.LmfPtr, Is.Zero);
    }
}
