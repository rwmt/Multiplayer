using System;
using Multiplayer.Client.Persistent;

namespace Multiplayer.Client;

public class SyncSessionWithTransferablesMarker
{
    private static ISessionWithTransferables thingFilterContext;

    public static ISessionWithTransferables DrawnThingFilter
    {
        get => thingFilterContext;
        set
        {
            if (value != null && thingFilterContext != null)
                throw new Exception("Session with transferables context already set!");

            thingFilterContext = value;
        }
    }
}
