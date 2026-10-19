using RimWorld;
using UnityEngine;

namespace Multiplayer.Client.Persistent;

public class MapPortalProxy(MapPortal portal) : Dialog_EnterPortal(portal), ISwitchToMap
{
    public static MapPortalProxy drawing;
    public bool itemsReady = false;

    // Several portal sessions can exist on a map, find the one for this portal
    public MapPortalSession Session => MapPortalSession.GetSessionFor(portal);

    public override void DoWindowContents(Rect inRect)
    {
        drawing = this;
        var session = Session;
        SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = session;

        try
        {
            if (session == null || !portal.Spawned)
            {
                Close();
                return;
            }

            base.DoWindowContents(inRect);
        }
        finally
        {
            drawing = null;
            SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = null;
        }
    }
}
