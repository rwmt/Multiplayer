using Multiplayer.Client;
using NUnit.Framework;

namespace ClientTests;

[TestFixture]
public class ReloadOptimizationTest
{
    [Test]
    public void NoneMode_DoesNotDeferFactionMapDrawerRebuild()
    {
        Assert.That(
            ReloadOptimization.ShouldDeferFactionMapDrawerRebuild(ReloadOptimizationMode.None),
            Is.False
        );
    }

    [Test]
    public void SnapshotMode_DefersFactionMapDrawerRebuild()
    {
        Assert.That(
            ReloadOptimization.ShouldDeferFactionMapDrawerRebuild(
                ReloadOptimizationMode.DeferFactionMapDrawerRebuildForSnapshot
            ),
            Is.True
        );
    }
}
