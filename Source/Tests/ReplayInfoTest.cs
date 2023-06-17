using System.Text;
using Multiplayer.Common;

namespace Tests;

public class ReplayInfoTest
{
    [Test]
    public void Test()
    {
        var replayInfo = new ReplayInfo
        {
            name = "test"
        };

        var xml = ReplayInfo.Write(replayInfo);
        Console.WriteLine(Encoding.UTF8.GetString(xml));

        var readInfo = ReplayInfo.Read(xml);
        Assert.That(readInfo.name, Is.EqualTo(replayInfo.name));
    }
}
