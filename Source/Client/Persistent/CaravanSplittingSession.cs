using System.Collections.Generic;
using JetBrains.Annotations;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Multiplayer.API;
using Multiplayer.Client.Experimental;
using Verse.Sound;

namespace Multiplayer.Client.Persistent
{
    /// <summary>
    /// Represents an active Caravan Split session. This session will track all the pawns and items being split.
    /// </summary>
    public class CaravanSplittingSession : ExposableSession, ISessionWithTransferables, IPausingWithDialog, ISessionWithCreationRestrictions
    {
        public override Map Map => null;

        /// <summary>
        /// The list of items that can be transferred, along with their count.
        /// </summary>
        private List<TransferableOneWay> transferables;

        /// <summary>
        /// Flag used to indicate that the ui needs to be redrawn.
        /// </summary>
        public bool uiDirty;

        /// <summary>
        /// The caravan being split.
        /// </summary>
        public Caravan Caravan { get; private set; }

        /// <summary>
        /// Reference to the dialog that is being displayed.
        /// </summary>
        public CaravanSplittingProxy dialog;

        public CaravanSplittingSession(Map map) : base(null)
        {
        }

        /// <summary>
        /// Handles creation of new CaravanSplittingSession.
        /// </summary>
        /// <param name="caravan"></param>
        public CaravanSplittingSession(Caravan caravan) : base(null)
        {
            Caravan = caravan;

            AddItems();
        }

        private void AddItems()
        {
            CaravanSplittingProxy.CreatingProxy = true;
            dialog = new CaravanSplittingProxy(Caravan) {
                session = this
            };
            CaravanSplittingProxy.CreatingProxy = false;
            dialog.CalculateAndRecacheTransferables();
            transferables = dialog.transferables;

            Find.WindowStack.Add(dialog);
        }

        /// <summary>
        /// Opens the dialog for a currently ongoing session. This should only be called
        /// when the dialog has been closed but the session still running.
        /// I.E. one player has closed the window without accepting/cancelling the session.
        /// </summary>
        public void OpenWindow(bool sound = true)
        {
            dialog = PrepareDialogProxy();
            if (!sound)
                dialog.soundAppear = null;

            CaravanUIUtility.CreateCaravanTransferableWidgets(
                transferables,
                out dialog.pawnsTransfer,
                out dialog.itemsTransfer,
                out dialog.foodAndMedicineTransfer,
                "SplitCaravanThingCountTip".Translate(),
                IgnorePawnsInventoryMode.Ignore,
                () => dialog.DestMassCapacity - dialog.DestMassUsage,
                false,
                Caravan.Tile
            );

            dialog.CountToTransferChanged();

            Find.WindowStack.Add(dialog);
        }

        private CaravanSplittingProxy PrepareDialogProxy()
        {
            CaravanSplittingProxy.CreatingProxy = true;
            var newProxy = new CaravanSplittingProxy(Caravan)
            {
                transferables = transferables,
                session = this
            };
            CaravanSplittingProxy.CreatingProxy = false;

            return newProxy;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
        }

        /// <summary>
        /// Find Transferable by thingId
        /// </summary>
        public Transferable GetTransferableByThingId(int thingId)
        {
            return transferables.Find(tr => tr.things.Any(t => t.thingIDNumber == thingId));
        }

        /// <summary>
        /// Sets the uiDirty flag.
        /// </summary>
        /// <param name="tr"></param>
        public void Notify_CountChanged(Transferable tr)
        {
            uiDirty = true;
        }

        /// <summary>
        /// Cancel a splitting session without accepting it. Closes the dialog and frees Multiplayer.WorldComp.splitSession
        /// </summary>
        [SyncMethod]
        public void CancelSplittingSession() {
            dialog.Close();
            Multiplayer.WorldComp.sessionManager.RemoveSession(this);
        }

        /// <summary>
        /// Resets the counts on all the transferables to 0.
        /// </summary>
        [SyncMethod]
        public void ResetSplittingSession()
        {
            transferables.ForEach(t => t.CountToTransfer = 0);
            uiDirty = true;
        }

        /// <summary>
        /// Accept the splitting session, split the caravan in to two caravans, free Multiplayer.WorldComp.splitSession and close the dialog.
        /// If the caravan fails to split, nothing will happen.
        /// </summary>
        [SyncMethod]
        public void AcceptSplitSession()
        {
            if (dialog.TrySplitCaravan())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                dialog.Close(false);
                Multiplayer.WorldComp.sessionManager.RemoveSession(this);
            }
        }

        public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
        {
            if (!Caravan.pawns.Contains(entry.pawn))
                return null;

            return new FloatMenuOption("MpCaravanSplittingSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                CameraJumper.TryJumpAndSelect(entry.pawn);
                OpenWindow();
            });
        }

        public override bool IsCurrentlyPausing(Map map) => true;

        public bool CanExistWith(Session other) => other is not CaravanSplittingSession;
    }
}
