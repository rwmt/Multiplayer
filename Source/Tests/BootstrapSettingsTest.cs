using Multiplayer.Common;
using Multiplayer.Common.Util;

namespace Tests;

public class BootstrapSettingsTest
{
    [Test]
    public void SharedTomlSerializerIncludesJoinCriticalFields()
    {
        var settings = new ServerSettings
        {
            gameName = "Bootstrap Test",
            lanAddress = "192.168.1.15",
            direct = true,
            lan = true,
        };

        var toml = TomlSettings.Serialize(settings);

        Assert.That(toml, Does.Contain("gameName = \"Bootstrap Test\""));
        Assert.That(toml, Does.Contain("lanAddress = \"192.168.1.15\""));
    }
}
