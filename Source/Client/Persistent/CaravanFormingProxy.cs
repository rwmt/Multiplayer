using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class CaravanFormingProxy : Dialog_FormCaravan
    {
        public static CaravanFormingProxy drawing;

        public CaravanFormingSession Session => map.MpComp().caravanForming;

        public CaravanFormingProxy(Map map, bool reform = false, Action onClosed = null, bool mapAboutToBeRemoved = false) : base(map, reform, onClosed, mapAboutToBeRemoved)
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
                    startingTile = session.startingTile;
                    destinationTile = session.destinationTile;

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
