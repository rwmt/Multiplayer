using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class TransporterLoadingProxy : Dialog_LoadTransporters, ISwitchToMap
    {
        public static TransporterLoadingProxy drawing;

        public bool itemsReady;

        public TransporterLoading Session => map.MpComp().transporterLoading;

        public TransporterLoadingProxy(Map map, List<CompTransporter> transporters) : base(map, transporters)
        {
        }

        public override void DoWindowContents(Rect inRect)
        {
            drawing = this;

            try
            {
                var session = Session;

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
            }
        }
    }

}
