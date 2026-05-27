using Multiplayer.Client;
using NUnit.Framework;

namespace ClientTests;

[TestFixture]
public class ReloadOptimizationTest
{
    [Test]
    public void NormalReload_RestoresFactionWithImmediateMapRedrawOnly()
    {
        var plan = ReloadOptimization.PlanFor(ReloadOptimizationMode.None);

        Assert.That(
            plan.RegenerateMapDrawersWhenRestoringFaction,
            Is.True
        );
        Assert.That(
            plan.RegenerateMapDrawersAfterSnapshot,
            Is.False
        );
    }

    [Test]
    public void JoinPointSnapshotReload_RestoresFactionWithoutRedrawAndCompletesAfterSnapshot()
    {
        var plan = ReloadOptimization.PlanFor(ReloadOptimizationMode.ForJoinPointSnapshot);

        Assert.That(
            plan.RegenerateMapDrawersWhenRestoringFaction,
            Is.False
        );
        Assert.That(
            plan.RegenerateMapDrawersAfterSnapshot,
            Is.True
        );
    }

    [Test]
    public void NormalReloadCompletion_DoesNotRunDeferredMapRedraw()
    {
        var redrawCount = 0;

        ReloadOptimization.Complete(ReloadOptimizationMode.None, () => redrawCount++);

        Assert.That(redrawCount, Is.EqualTo(0));
    }

    [Test]
    public void JoinPointSnapshotCompletion_RunsOneDeferredMapRedraw()
    {
        var redrawCount = 0;

        ReloadOptimization.Complete(ReloadOptimizationMode.ForJoinPointSnapshot, () => redrawCount++);

        Assert.That(redrawCount, Is.EqualTo(1));
    }
}
