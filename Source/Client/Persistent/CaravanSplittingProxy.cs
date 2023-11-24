using Multiplayer.Client.Util;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace Multiplayer.Client.Persistent
{
    /// <summary>
    /// Multiplayer replacement of the Dialog_SplitCaravan dialog.
    /// </summary>
    public class CaravanSplittingProxy : Dialog_SplitCaravan, ISwitchToMap
    {
        public static bool CreatingProxy;

        /// <summary>
        /// Reference to this proxy's CaravanSplittingSession.
        /// </summary>
        public CaravanSplittingSession session;

        /// <summary>
        /// Handles creation of a CaravanSplittingProxy.
        /// </summary>
        /// <param name="caravan"></param>
        public CaravanSplittingProxy(Caravan caravan) : base(caravan)
        {
            this.caravan = caravan;
        }

        public override void PostOpen()
        {
            // Taken from Window.PostOpen, overriden to remove effects of Dialog_SplitCaravan.PostOpen

            if (soundAppear != null)
                soundAppear.PlayOneShotOnCamera();

            if (soundAmbient != null)
                sustainerAmbient = soundAmbient.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.PerFrame));
        }

        /// <summary>
        /// Override of Dialog_SplitCaravan.DoWindowContents that calls into this dialog's DoBottomButtons.
        /// This was needed because Dialog_SplitCaravan.DoBottomButtons isn't virtual, but needed to be overridden.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = session;

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

                Rect rect = new Rect(0f, 0f, inRect.width, 35f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "SplitCaravan".Translate());
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                CaravanUIUtility.DrawCaravanInfo(new CaravanUIUtility.CaravanInfo(SourceMassUsage, SourceMassCapacity, cachedSourceMassCapacityExplanation, SourceTilesPerDay, cachedSourceTilesPerDayExplanation, SourceDaysWorthOfFood, SourceForagedFoodPerDay, cachedSourceForagedFoodPerDayExplanation, SourceVisibility, cachedSourceVisibilityExplanation), new CaravanUIUtility.CaravanInfo(DestMassUsage, DestMassCapacity, cachedDestMassCapacityExplanation, DestTilesPerDay, cachedDestTilesPerDayExplanation, DestDaysWorthOfFood, DestForagedFoodPerDay, cachedDestForagedFoodPerDayExplanation, DestVisibility, cachedDestVisibilityExplanation), caravan.Tile, caravan.pather.Moving ? TicksToArrive : null, -9999f, new Rect(12f, 35f, inRect.width - 24f, 40f));
                tabsList.Clear();
                tabsList.Add(new TabRecord("PawnsTab".Translate(), delegate
                {
                    tab = Tab.Pawns;
                }, tab == Tab.Pawns));
                tabsList.Add(new TabRecord("ItemsTab".Translate(), delegate
                {
                    tab = Tab.Items;
                }, tab == Tab.Items));
                tabsList.Add(new TabRecord("TravelSupplies".Translate(), delegate
                {
                    tab = Tab.FoodAndMedicine;
                }, tab == Tab.FoodAndMedicine));
                inRect.yMin += 119f;
                Widgets.DrawMenuSection(inRect);
                TabDrawer.DrawTabs(inRect, tabsList, 200f);
                inRect = inRect.ContractedBy(17f);
                Widgets.BeginGroup(inRect);
                Rect rect2 = inRect.AtZero();
                DoBottomButtons(rect2);
                Rect inRect2 = rect2;
                inRect2.yMax -= 59f;
                bool flag = false;
                switch (tab)
                {
                    case Tab.Pawns:
                        pawnsTransfer.OnGUI(inRect2, out flag);
                        break;
                    case Tab.Items:
                        itemsTransfer.OnGUI(inRect2, out flag);
                        break;
                    case Tab.FoodAndMedicine:
                        foodAndMedicineTransfer.OnGUI(inRect2, out flag);
                        break;
                }
                if (flag)
                {
                    CountToTransferChanged();
                }
                Widgets.EndGroup();
            }
            finally
            {
                SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = null;
            }
        }

        /// <summary>
        /// Replaces Dialog_SplitCaravan.DoBottomButtons.
        /// This is a copy of the original but with the handlers for the buttons pulled out into separate handlers.
        /// </summary>
        /// <param name="rect"></param>
        private new void DoBottomButtons(Rect rect)
        {
            Rect acceptRect = new Rect(rect.width / 2f - BottomButtonSize.x / 2f, rect.height - 55f, BottomButtonSize.x, BottomButtonSize.y);
            if (Widgets.ButtonText(acceptRect, "AcceptButton".Translate(), true, false))
            {
                AcceptButtonClicked();
            }

            Rect resetRect = new Rect(acceptRect.x - 10f - BottomButtonSize.x, acceptRect.y, BottomButtonSize.x, BottomButtonSize.y);
            if (Widgets.ButtonText(resetRect, "ResetButton".Translate(), true, false))
            {
                ResetButtonClicked();
            }

            using (MpStyle.Set(new Color(1f, 0.3f, 0.35f)))
            {
                Rect cancelRect = new Rect(acceptRect.xMax + 10f, acceptRect.y, BottomButtonSize.x, BottomButtonSize.y);
                if (Widgets.ButtonText(cancelRect, "CancelButton".Translate(), true, false))
                {
                    CancelButtonClicked();
                }
            }
        }

        private void AcceptButtonClicked()
        {
            session.AcceptSplitSession();
        }

        private void CancelButtonClicked()
        {
            session.CancelSplittingSession();
        }

        private void ResetButtonClicked()
        {
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            session.ResetSplittingSession();
        }
    }
}
