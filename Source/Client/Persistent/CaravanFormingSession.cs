using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class CaravanFormingSession : IExposable, ISessionWithTransferables
    {
        public Map map;

        public int sessionId;
        public bool reform;
        public Action onClosed;
        public bool mapAboutToBeRemoved;
        public int startingTile = -1;
        public int destinationTile = -1;
        public List<TransferableOneWay> transferables;

        public bool uiDirty;

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

            AddItems();
        }

        private void AddItems()
        {
            var dialog = new CaravanFormingProxy(map, reform, null, mapAboutToBeRemoved);
            dialog.autoSelectTravelSupplies = false;
            dialog.CalculateAndRecacheTransferables();
            transferables = dialog.transferables;
        }

        public void OpenWindow(bool sound = true)
        {
            Find.Selector.ClearSelection();

            var dialog = PrepareDummyDialog();
            if (!sound)
                dialog.soundAppear = null;
            dialog.doCloseX = true;

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

            dialog.CountToTransferChanged();

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
                autoSelectTravelSupplies = false,
            };

            return dialog;
        }

        [SyncMethod]
        public void ChooseRoute(int destinationTile)
        {
            var dialog = PrepareDummyDialog();
            dialog.Notify_ChoseRoute(destinationTile);

            startingTile = dialog.startingTile;
            this.destinationTile = dialog.destinationTile;

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
