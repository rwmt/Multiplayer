using System.Text;

namespace Tests;

[TestFixture]
public class LineEndingNormalizationTest
{
    private static string Normalize(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        var len = Multiplayer.Common.Utils.NormalizeLineEndingsInPlace(data);
        return Encoding.UTF8.GetString(data, 0, len);
    }

    [Test]
    public void WindowsAndOldMacEndingsBecomeLf()
    {
        const string lf = "<Defs>\n  <ThingDef/>\n</Defs>\n";

        Assert.That(Normalize("<Defs>\r\n  <ThingDef/>\r\n</Defs>\r\n"), Is.EqualTo(lf));
        Assert.That(Normalize("<Defs>\r  <ThingDef/>\r</Defs>\r"), Is.EqualTo(lf));
        Assert.That(Normalize(lf), Is.EqualTo(lf));
    }

    [Test]
    public void MixedEndingsAndBlankLines()
    {
        Assert.That(Normalize("a\r\n\r\nb\r\rc\n\rd"), Is.EqualTo("a\n\nb\n\nc\n\nd"));
        Assert.That(Normalize("\r"), Is.EqualTo("\n"));
        Assert.That(Normalize("\r\n"), Is.EqualTo("\n"));
        Assert.That(Normalize(""), Is.EqualTo(""));
    }

    [Test]
    public void OtherBytesAreKept()
    {
        Assert.That(Normalize("ünïcødé\tтекст"), Is.EqualTo("ünïcødé\tтекст"));
    }
}
