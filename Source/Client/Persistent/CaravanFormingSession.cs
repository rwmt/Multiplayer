using Multiplayer.API;
using Multiplayer.Client.Persistent;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Multiplayer.Client.Experimental;
using Verse;

namespace Multiplayer.Client
{
    public class CaravanFormingSession : ExposableSession, ISessionWithTransferables, IPausingWithDialog, ISessionWithCreationRestrictions
    {
        public Map map;

        public bool reform;
        public Action onClosed;
        public bool mapAboutToBeRemoved;
        public int startingTile = -1;
        public int destinationTile = -1;
        public List<TransferableOneWay> transferables;
        public bool autoSelectTravelSupplies;
        public IntVec3? meetingSpot;

        public bool uiDirty;

        public override Map Map => map;

        public CaravanFormingSession(Map map) : base(map)
        {
            this.map = map;
        }

        public CaravanFormingSession(Map map, bool reform, Action onClosed, bool mapAboutToBeRemoved, IntVec3? meetingSpot = null) : this(map)
        {
            this.reform = reform;
            this.onClosed = onClosed;
            this.mapAboutToBeRemoved = mapAboutToBeRemoved;
            autoSelectTravelSupplies = !reform;
            this.meetingSpot = meetingSpot;

            // Should this be called from PostAddSession? It would also be called from the other constructor
            // (map only parameter) - do we want that to happen? Is it going to come up?
            AddItems();
        }

        private void AddItems()
        {
            var dialog = new CaravanFormingProxy(map, reform, null, mapAboutToBeRemoved, meetingSpot)
            {
                autoSelectTravelSupplies = autoSelectTravelSupplies
            };
            dialog.CalculateAndRecacheTransferables();
            transferables = dialog.transferables;
        }

        public void OpenWindow(bool sound = true)
        {
            Log.Message($"session {sessionId}");

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
            var dialog = new CaravanFormingProxy(map, reform, null, mapAboutToBeRemoved, meetingSpot)
            {
                transferables = transferables,
                startingTile = startingTile,
                destinationTile = destinationTile,
                thisWindowInstanceEverOpened = true,
                autoSelectTravelSupplies = autoSelectTravelSupplies,
            };

            if (autoSelectTravelSupplies)
                dialog.SelectApproximateBestTravelSupplies();

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
            map.MpComp().sessionManager.RemoveSession(this);
            Find.WorldRoutePlanner.Stop();
        }

        [SyncMethod]
        public void SetAutoSelectTravelSupplies(bool value)
        {
            if (autoSelectTravelSupplies != value)
            {
                autoSelectTravelSupplies = value;
                uiDirty = true;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref reform, "reform");
            Scribe_Values.Look(ref onClosed, "onClosed");
            Scribe_Values.Look(ref mapAboutToBeRemoved, "mapAboutToBeRemoved");
            Scribe_Values.Look(ref startingTile, "startingTile");
            Scribe_Values.Look(ref destinationTile, "destinationTile");
            Scribe_Values.Look(ref autoSelectTravelSupplies, "autoSelectTravelSupplies");
            Scribe_Values.Look(ref meetingSpot, "meetingSpot");

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

        public override bool IsCurrentlyPausing(Map map) => map == this.map;

        public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
        {
            return new FloatMenuOption("MpCaravanFormingSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                OpenWindow();
            });
        }

        public bool CanExistWith(Session other) => other is not CaravanFormingSession;
    }

}
