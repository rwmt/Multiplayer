using Multiplayer.Client.Patches;

namespace Tests;

public class ViewedMapTrackerTest
{
    [Test]
    public void ReportsInitialWorldViewWithoutVisitingATileMap()
    {
        var tracker = new ViewedMapTracker();
        var sent = new List<(int, int)>();

        tracker.Report(-2, (previous, current) => sent.Add((previous, current)));

        Assert.That(sent, Is.EqualTo(new[] { (-1, -2) }));
    }

    [Test]
    public void ReportsInitialMapAndPreservesTransitionChainAfterReload()
    {
        var tracker = new ViewedMapTracker();
        var sent = new List<(int, int)>();
        void Send(int previous, int current) => sent.Add((previous, current));

        tracker.Report(0, Send);
        tracker.Report(0, Send);
        // A reload changes the local map without sending a transition.
        tracker.Report(-2, Send);
        tracker.Report(5, Send);

        Assert.That(sent, Is.EqualTo(new[] { (-1, 0), (0, -2), (-2, 5) }));
    }

    [Test]
    public void FailedSendDoesNotAdvanceReportedMap()
    {
        var tracker = new ViewedMapTracker();
        Assert.Throws<InvalidOperationException>(() => tracker.Report(0, (_, _) => throw new InvalidOperationException()));
        var sent = new List<(int, int)>();

        tracker.Report(1, (previous, current) => sent.Add((previous, current)));

        Assert.That(sent, Is.EqualTo(new[] { (-1, 1) }));
    }

    [Test]
    public void NewSessionStartsWithUnknownMapAgain()
    {
        var tracker = new ViewedMapTracker();
        var sent = new List<(int, int)>();
        void Send(int previous, int current) => sent.Add((previous, current));
        tracker.Report(0, Send);
        tracker.Reset();
        tracker.Report(0, Send);

        Assert.That(sent, Is.EqualTo(new[] { (-1, 0), (-1, 0) }));
    }
}
