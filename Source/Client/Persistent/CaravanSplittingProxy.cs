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
                soundAppear.PlayOneShotOnCamera(null);

            if (soundAmbient != null)
                sustainerAmbient = soundAmbient.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.PerFrame));
        }

        /// <summary>
        /// Override of Dialog_SplitCaravan.DoWindowContents that calls into this dialog's DoBottomButtons.
        /// This was needed because Dialog_SplitCaravan.DoBottomButtons isn't virtual, but needed to be overridden.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
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
            CaravanUIUtility.DrawCaravanInfo(new CaravanUIUtility.CaravanInfo(SourceMassUsage, SourceMassCapacity, cachedSourceMassCapacityExplanation, SourceTilesPerDay, cachedSourceTilesPerDayExplanation, SourceDaysWorthOfFood, SourceForagedFoodPerDay, cachedSourceForagedFoodPerDayExplanation, SourceVisibility, cachedSourceVisibilityExplanation, -1f, -1f, null), new CaravanUIUtility.CaravanInfo(DestMassUsage, DestMassCapacity, cachedDestMassCapacityExplanation, DestTilesPerDay, cachedDestTilesPerDayExplanation, DestDaysWorthOfFood, DestForagedFoodPerDay, cachedDestForagedFoodPerDayExplanation, DestVisibility, cachedDestVisibilityExplanation, -1f, -1f, null), caravan.Tile, (!caravan.pather.Moving) ? null : new int?(TicksToArrive), -9999f, new Rect(12f, 35f, inRect.width - 24f, 40f), true, null, false);
            tabsList.Clear();
            tabsList.Add(new TabRecord("PawnsTab".Translate(), delegate
            {
                tab = Tab.Pawns;
            }, tab == Tab.Pawns));
            tabsList.Add(new TabRecord("ItemsTab".Translate(), delegate
            {
                tab = Tab.Items;
            }, tab == Tab.Items));
            tabsList.Add(new TabRecord("FoodAndMedicineTab".Translate(), delegate
            {
                tab = Tab.FoodAndMedicine;
            }, tab == Tab.FoodAndMedicine));
            inRect.yMin += 119f;
            Widgets.DrawMenuSection(inRect);
            TabDrawer.DrawTabs(inRect, tabsList, 200f);
            inRect = inRect.ContractedBy(17f);
            GUI.BeginGroup(inRect);
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
            GUI.EndGroup();
        }

        /// <summary>
        /// Replaces Dialog_SplitCaravan.DoBottomButtons.
        /// This is a copy of the original but with the handlers for the buttons pulled out into separate handlers.
        /// </summary>
        /// <param name="rect"></param>
        private new void DoBottomButtons(Rect rect)
        {
            float num = rect.width / 2f;
            Vector2 bottomButtonSize = BottomButtonSize;
            float x = num - bottomButtonSize.x / 2f;
            float y = rect.height - 55f;
            Vector2 bottomButtonSize2 = BottomButtonSize;
            float x2 = bottomButtonSize2.x;
            Vector2 bottomButtonSize3 = BottomButtonSize;
            Rect rect2 = new Rect(x, y, x2, bottomButtonSize3.y);
            if (Widgets.ButtonText(rect2, "AcceptButton".Translate(), true, false, true))
            {
                AcceptButtonClicked();
            }
            float num2 = rect2.x - 10f;
            Vector2 bottomButtonSize4 = BottomButtonSize;
            float x3 = num2 - bottomButtonSize4.x;
            float y2 = rect2.y;
            Vector2 bottomButtonSize5 = BottomButtonSize;
            float x4 = bottomButtonSize5.x;
            Vector2 bottomButtonSize6 = BottomButtonSize;
            Rect rect3 = new Rect(x3, y2, x4, bottomButtonSize6.y);
            if (Widgets.ButtonText(rect3, "ResetButton".Translate(), true, false, true))
            {
                ResetButtonClicked();
            }
            float x5 = rect2.xMax + 10f;
            float y3 = rect2.y;
            Vector2 bottomButtonSize7 = BottomButtonSize;
            float x6 = bottomButtonSize7.x;
            Vector2 bottomButtonSize8 = BottomButtonSize;
            Rect rect4 = new Rect(x5, y3, x6, bottomButtonSize8.y);
            if (Widgets.ButtonText(rect4, "CancelButton".Translate(), true, false, true))
            {
                CancelButtonClicked();
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
