using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client;

public class ServerSettingsClient : IExposable
{
    public ServerSettings settings = new();

    public void ExposeData()
    {
        settings.ExposeData();
    }
}
