using Multiplayer.Common;

namespace Tests;

[TestFixture]
public class LogTrimTest
{
    private static string Lines(int from, int to) =>
        string.Join("\n", Enumerable.Range(from, to - from + 1).Select(i => $"line {i}"));

    [Test]
    public void ShortTextIsKept()
    {
        var text = Lines(1, 5);
        Assert.That(text.KeepHeadAndTailLines(2, 3), Is.SameAs(text));
    }

    [Test]
    public void LongTextKeepsHeadAndTail()
    {
        var trimmed = Lines(1, 10).KeepHeadAndTailLines(2, 3);

        Assert.That(trimmed, Is.EqualTo(Lines(1, 2) + "\n(... 5 lines omitted ...)\n" + Lines(8, 10)));
    }

    [Test]
    public void TrailingNewlineCountsAsAnEmptyLastLine()
    {
        var trimmed = (Lines(1, 10) + "\n").KeepHeadAndTailLines(2, 3);

        Assert.That(trimmed, Is.EqualTo(Lines(1, 2) + "\n(... 6 lines omitted ...)\n" + Lines(9, 10) + "\n"));
    }
}
