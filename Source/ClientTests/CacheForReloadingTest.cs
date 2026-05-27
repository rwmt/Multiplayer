using Multiplayer.Client;
using NUnit.Framework;

namespace ClientTests;

[TestFixture]
public class CacheForReloadingTest
{
    [Test]
    public void NoneMode_DoesNotDeferFactionMapDrawerRebuild()
    {
        Assert.That(
            CacheForReloading.ShouldDeferFactionMapDrawerRebuild(ReloadOptimizationMode.None),
            Is.False
        );
    }

    [Test]
    public void SnapshotMode_DefersFactionMapDrawerRebuild()
    {
        Assert.That(
            CacheForReloading.ShouldDeferFactionMapDrawerRebuild(
                ReloadOptimizationMode.DeferFactionMapDrawerRebuildForSnapshot
            ),
            Is.True
        );
    }
}
