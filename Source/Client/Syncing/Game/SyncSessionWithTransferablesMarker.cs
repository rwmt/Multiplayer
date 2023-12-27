using System;
using Multiplayer.API;

namespace Multiplayer.Client;

public class SyncSessionWithTransferablesMarker
{
    private static ISessionWithTransferables drawnSessionWithTransferables;

    public static ISessionWithTransferables DrawnSessionWithTransferables
    {
        get => drawnSessionWithTransferables;
        set
        {
            if (value != null && drawnSessionWithTransferables != null)
                throw new Exception("Session with transferables context already set!");

            drawnSessionWithTransferables = value;
        }
    }
}
