using Multiplayer.Common.ChatCommands;

namespace Tests;

[TestFixture]
public class CommandTokenizerTest
{
    [TestCase("", new string[0])]
    [TestCase("   ", new string[0])]
    [TestCase("speed 3", new[] { "speed", "3" })]
    [TestCase("  speed   3  ", new[] { "speed", "3" })]
    [TestCase("whois PlayerName", new[] { "whois", "PlayerName" })]
    [TestCase("announce raid soon", new[] { "announce", "raid", "soon" })]
    [TestCase("help ?", new[] { "help", "?" })]
    public void Tokenize_PlainCommandText_ReturnsWhitespaceSeparatedArguments(string input, string[] expected)
    {
        Assert.That(CommandTokenizer.TryTokenize(input, out var tokens, out var error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(tokens, Is.EqualTo(expected));
    }

    [TestCase("whois \"Player Name\"", new[] { "whois", "Player Name" })]
    [TestCase("announce \"raid soon\"", new[] { "announce", "raid soon" })]
    [TestCase("kick \"\"", new[] { "kick", "" })]
    [TestCase("announce \"hello \\\"world\\\"\"", new[] { "announce", "hello \"world\"" })]
    [TestCase("announce \"C:\\\\Games\\\\RimWorld\"", new[] { "announce", "C:\\Games\\RimWorld" })]
    [TestCase("command pre\"quoted middle\"post", new[] { "command", "prequoted middlepost" })]
    [TestCase("command \"quoted\"plain", new[] { "command", "quotedplain" })]
    [TestCase("command plain\"quoted\"", new[] { "command", "plainquoted" })]
    public void Tokenize_QuotedCommandText_ReturnsQuotedContentAsArgument(string input, string[] expected)
    {
        Assert.That(CommandTokenizer.TryTokenize(input, out var tokens, out var error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(tokens, Is.EqualTo(expected));
    }

    [TestCase("whois \"Player Name")]
    [TestCase("announce raid \"soon")]
    [TestCase("\"")]
    public void Tokenize_MissingClosingQuote_ReturnsHelpfulError(string input)
    {
        Assert.That(CommandTokenizer.TryTokenize(input, out var tokens, out var error), Is.False);
        Assert.That(tokens, Is.Empty);
        Assert.That(error, Is.EqualTo("Invalid command arguments: missing closing quote."));
    }
}
