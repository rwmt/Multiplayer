using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using Verse;
using Multiplayer.Client.Persistent;

namespace Multiplayer.Client
{
    public class TransporterLoading : IExposable, ISessionWithTransferables, IPausingWithDialog
    {
        public int SessionId => sessionId;
        public Map Map => map;

        public int sessionId;
        public Map map;

        public List<CompTransporter> transporters;
        public List<ThingWithComps> pods;
        public List<TransferableOneWay> transferables;

        public bool uiDirty;

        public TransporterLoading(Map map)
        {
            this.map = map;
        }

        public TransporterLoading(Map map, List<CompTransporter> transporters) : this(map)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();
            this.transporters = transporters;
            pods = transporters.Select(t => t.parent).ToList();

            AddItems();
        }

        private void AddItems()
        {
            var dialog = new TransporterLoadingProxy(map, transporters);

            // Init code taken from Dialog_LoadTransporters.PostOpen
            dialog.CalculateAndRecacheTransferables();
            if (dialog.CanChangeAssignedThingsAfterStarting && dialog.LoadingInProgressOrReadyToLaunch)
                dialog.SetLoadedItemsToLoad();

            transferables = dialog.transferables;
        }

        [SyncMethod]
        public void TryAccept()
        {
            if (PrepareDummyDialog().TryAccept())
                Remove();
        }

        [SyncMethod(debugOnly = true)]
        public void DebugTryLoadInstantly()
        {
            if (PrepareDummyDialog().DebugTryLoadInstantly())
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
            map.MpComp().transporterLoading = null;
        }

        public void OpenWindow(bool sound = true)
        {
            var dialog = PrepareDummyDialog();
            if (!sound)
                dialog.soundAppear = null;

            Find.WindowStack.Add(dialog);
        }

        private TransporterLoadingProxy PrepareDummyDialog()
        {
            return new TransporterLoadingProxy(map, transporters)
            {
                itemsReady = true,
                transferables = transferables
            };
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
            Scribe_Collections.Look(ref pods, "transporters", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                transporters = pods.Select(t => t.GetComp<CompTransporter>()).ToList();
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
