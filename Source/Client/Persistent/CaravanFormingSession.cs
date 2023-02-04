using Multiplayer.API;
using Multiplayer.Client.Persistent;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{
    public class CaravanFormingSession : IExposable, ISessionWithTransferables, IPausingWithDialog
    {
        public Map map;

        public int sessionId;
        public bool reform;
        public Action onClosed;
        public bool mapAboutToBeRemoved;
        public int startingTile = -1;
        public int destinationTile = -1;
        public List<TransferableOneWay> transferables;
        public bool autoSelectTravelSupplies;

        public bool uiDirty;

        public Map Map => map;
        public int SessionId => sessionId;

        public CaravanFormingSession(Map map)
        {
            this.map = map;
        }

        public CaravanFormingSession(Map map, bool reform, Action onClosed, bool mapAboutToBeRemoved) : this(map)
        {
            //sessionId = map.MpComp().mapIdBlock.NextId();
            sessionId = Multiplayer.GlobalIdBlock.NextId();

            this.reform = reform;
            this.onClosed = onClosed;
            this.mapAboutToBeRemoved = mapAboutToBeRemoved;
            autoSelectTravelSupplies = !reform;

            AddItems();
        }

        private void AddItems()
        {
            var dialog = new CaravanFormingProxy(map, reform, null, mapAboutToBeRemoved)
            {
                autoSelectTravelSupplies = autoSelectTravelSupplies
            };
            dialog.CalculateAndRecacheTransferables();
            transferables = dialog.transferables;
        }

        public void OpenWindow(bool sound = true)
        {
            var dialog = PrepareDummyDialog();
            if (!sound)
                dialog.soundAppear = null;

            CaravanUIUtility.CreateCaravanTransferableWidgets(
                transferables,
                out dialog.pawnsTransfer,
                out dialog.itemsTransfer,
                out dialog.travelSuppliesTransfer,
                "FormCaravanColonyThingCountTip".Translate(),
                dialog.IgnoreInventoryMode,
                () => dialog.MassCapacity - dialog.MassUsage,
                dialog.AutoStripSpawnedCorpses,
                dialog.CurrentTile,
                mapAboutToBeRemoved
            );

            dialog.Notify_TransferablesChanged();

            Find.WindowStack.Add(dialog);
        }

        private CaravanFormingProxy PrepareDummyDialog()
        {
            var dialog = new CaravanFormingProxy(map, reform, null, mapAboutToBeRemoved)
            {
                transferables = transferables,
                startingTile = startingTile,
                destinationTile = destinationTile,
                thisWindowInstanceEverOpened = true,
                autoSelectTravelSupplies = autoSelectTravelSupplies,
            };

            return dialog;
        }

        [SyncMethod]
        public void ChooseRoute(int destination)
        {
            var dialog = PrepareDummyDialog();
            dialog.Notify_ChoseRoute(destination);

            startingTile = dialog.startingTile;
            destinationTile = dialog.destinationTile;

            uiDirty = true;
        }

        [SyncMethod]
        public void TryReformCaravan()
        {
            if (PrepareDummyDialog().TryReformCaravan())
                Remove();
        }

        [SyncMethod]
        public void TryFormAndSendCaravan()
        {
            if (PrepareDummyDialog().TryFormAndSendCaravan())
                Remove();
        }

        [SyncMethod(debugOnly = true)]
        public void DebugTryFormCaravanInstantly()
        {
            if (PrepareDummyDialog().DebugTryFormCaravanInstantly())
                Remove();
        }

        [SyncMethod]
        public void Reset()
        {
            transferables.ForEach(t => t.CountToTransfer = 0);
            uiDirty = true;
        }

        [SyncMethod]
        public void Remove()
        {
            map.MpComp().caravanForming = null;
            Find.WorldRoutePlanner.Stop();
        }

        [SyncMethod]
        public void SetAutoSelectTravelSupplies(bool value)
        {
            if (autoSelectTravelSupplies != value)
            {
                autoSelectTravelSupplies = value;
                PrepareDummyDialog().SelectApproximateBestTravelSupplies();
                uiDirty = true;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref reform, "reform");
            Scribe_Values.Look(ref onClosed, "onClosed");
            Scribe_Values.Look(ref mapAboutToBeRemoved, "mapAboutToBeRemoved");
            Scribe_Values.Look(ref startingTile, "startingTile");
            Scribe_Values.Look(ref destinationTile, "destinationTile");

            Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
        }

        public Transferable GetTransferableByThingId(int thingId)
        {
            return transferables.Find(tr => tr.things.Any(t => t.thingIDNumber == thingId));
        }

        public void Notify_CountChanged(Transferable tr)
        {
            uiDirty = true;
        }
    }

}
