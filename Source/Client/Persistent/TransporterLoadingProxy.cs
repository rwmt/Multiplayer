using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class TransporterLoadingProxy : Dialog_LoadTransporters, ISwitchToMap
    {
        public bool itemsReady;

        public static TransporterLoadingProxy drawing;

        // Several transporter loading sessions can exist on a map, find the one for these transporters
        public TransporterLoading Session => TransporterLoading.GetSessionFor(map, transporters);

        public TransporterLoadingProxy(Map map, List<CompTransporter> transporters) : base(map, transporters)
        {
        }

        public override void DoWindowContents(Rect inRect)
        {
            var session = Session;
            SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = session;
            drawing = this;

            try
            {
                if (session == null)
                {
                    Close();
                }
                else if (session.uiDirty)
                {
                    CountToTransferChanged();
                    session.uiDirty = false;
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

}
