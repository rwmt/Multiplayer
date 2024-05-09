using RimWorld;
using UnityEngine;

namespace Multiplayer.Client.Persistent;

public class MapPortalProxy(MapPortal portal) : Dialog_EnterPortal(portal), ISwitchToMap
{
    public static MapPortalProxy drawing;
    public bool itemsReady = false;

    public MapPortalSession Session => portal.Map.MpComp().sessionManager.GetFirstOfType<MapPortalSession>();

    public override void DoWindowContents(Rect inRect)
    {
        drawing = this;
        var session = Session;
        SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = session;

        try
        {
            if (session == null)
                Close();

            base.DoWindowContents(inRect);
        }
        finally
        {
            drawing = null;
            SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = null;
        }
    }
}
