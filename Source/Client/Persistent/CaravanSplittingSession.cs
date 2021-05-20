using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Multiplayer.API;
using Verse.Sound;

namespace Multiplayer.Client.Persistent
{
    /// <summary> 
    /// Represents an active Caravan Split session. This session will track all the pawns and items being split.
    /// </summary>
    public class CaravanSplittingSession : IExposable, ISessionWithTransferables
    {
        private int sessionId;

        /// <summary>
        /// Uniquely identifies this ISessionWithTransferables
        /// </summary>
        public int SessionId => sessionId;

        /// <summary>
        /// The list of items that can be transferred, along with thier count.
        /// </summary>
        private List<TransferableOneWay> transferables;

        /// <summary>
        /// Flag used to indicate that the ui needs to be redrawn.
        /// </summary>
        public bool uiDirty;

        /// <summary>
        /// The caravan being split.
        /// </summary>
        private readonly Caravan caravan;
        public Caravan Caravan { get { return caravan; } }

        /// <summary>
        /// Reference to the dialog that is being displayed.
        /// </summary>
        public CaravanSplittingProxy dialog;

        /// <summary>
        /// Handles creation of new CaravanSplittingSession. 
        /// Ensures a unique Id is given to this session and creates the dialog.
        /// </summary>
        /// <param name="caravan"></param>
        private CaravanSplittingSession(Caravan caravan)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();
            this.caravan = caravan;

            AddItems();
        }

        private void AddItems()
        {
            CaravanSplittingProxy.CreatingProxy = true;
            dialog = new CaravanSplittingProxy(caravan) {
                session = this
            };
            CaravanSplittingProxy.CreatingProxy = false;
            dialog.doCloseX = true;
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
            Find.Selector.ClearSelection();

            dialog = PrepareDialogProxy();
            if (!sound)
                dialog.soundAppear = null;

            CaravanUIUtility.CreateCaravanTransferableWidgets_NewTmp(transferables, out dialog.pawnsTransfer, out dialog.itemsTransfer, out dialog.foodAndMedicineTransfer,
                    "SplitCaravanThingCountTip".Translate(), IgnorePawnsInventoryMode.Ignore, () => dialog.DestMassCapacity - dialog.DestMassUsage, false, caravan.Tile, false);
            dialog.CountToTransferChanged();

            Find.WindowStack.Add(dialog);
        }

        private CaravanSplittingProxy PrepareDialogProxy()
        {
            CaravanSplittingProxy.CreatingProxy = true;
            var newProxy = new CaravanSplittingProxy(caravan)
            {
                transferables = transferables,
                session = this
            };
            CaravanSplittingProxy.CreatingProxy = false;

            return newProxy;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");

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
        /// Factory method that creates a new CaravanSplittingSession and stores it to Multiplayer.WorldComp.splitSession
        /// Only one caravan split session can exist at a time.
        /// </summary>
        /// <param name="caravan"></param>
        [SyncMethod]
        public static void CreateSplittingSession(Caravan caravan)
        {
            //Start caravan splitting session here by calling new session constructor
            if (Multiplayer.WorldComp.splitSession == null)
            {
                Multiplayer.WorldComp.splitSession = new CaravanSplittingSession(caravan);
            }
        }

        /// <summary>
        /// Cancel a splitting session without accepting it. Closes the dialog and frees Multiplayer.WorldComp.splitSession 
        /// </summary>
        [SyncMethod]
        public static void CancelSplittingSession() {
            if (Multiplayer.WorldComp.splitSession != null) {
                Multiplayer.WorldComp.splitSession.dialog.Close();
                Multiplayer.WorldComp.splitSession = null;
            }
        }

        /// <summary>
        /// Resets the counts on all the tranferrables to 0.
        /// </summary>
        [SyncMethod]
        public static void ResetSplittingSession()
        {
            Multiplayer.WorldComp.splitSession.transferables.ForEach(t => t.CountToTransfer = 0);
            Multiplayer.WorldComp.splitSession.uiDirty = true;
        }

        /// <summary>
        /// Accept the splitting session, split the caravan in to two caravans, free Multiplayer.WorldComp.splitSession and close the dialog.
        /// If the caravan fails to split, nothing will happen.
        /// </summary>
        [SyncMethod]
        public static void AcceptSplitSession()
        {
            if ((Multiplayer.WorldComp.splitSession?.dialog.TrySplitCaravan()).Value)
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Multiplayer.WorldComp.splitSession.dialog.Close(false);
                Multiplayer.WorldComp.splitSession = null;
            }
        }
    }
}
