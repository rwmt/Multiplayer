using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent;

public class MapPortalSession : ExposableSession, ISessionWithTransferables, ISessionWithCreationRestrictions
{
    public MapPortal portal;
    public List<TransferableOneWay> transferables;

    public override Map Map => portal.Map;

    public MapPortalSession(Map _) : base(null)
    {
        // Mandatory constructor
    }

    public MapPortalSession(MapPortal portal) : base(null)
    {
        this.portal = portal;

        AddItems();
    }

    private void AddItems()
    {
        var dialog = new MapPortalProxy(portal);

        // Init code taken from Dialog_EnterPortal.PostOpen
        dialog.CalculateAndRecacheTransferables();

        transferables = dialog.transferables;
    }

    public override bool IsCurrentlyPausing(Map map) => Map == map;

    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
    {
        if (entry.map != Map)
            return null;

        return new FloatMenuOption("MpMapPortalSession".Translate(portal?.Label), () =>
        {
            SwitchToMapOrWorld(Map);
            OpenWindow();
        });
    }

    public static void OpenOrCreateSession(MapPortal portal)
    {
        var session = portal.Map.MpComp().sessionManager.AllSessions
            .OfType<MapPortalSession>()
            .FirstOrDefault(s => s.portal == portal);
        if (session == null)
            CreateSession(portal);
        else
            session.OpenWindow();
    }

    [SyncMethod]
    private static void CreateSession(MapPortal portal)
    {
        var map = portal.Map;
        var manager = map.MpComp().sessionManager;
        var session = manager.GetOrAddSession(new MapPortalSession(portal));

        // Shouldn't happen and is here for safety.
        if (session == null)
            Log.Error($"Failed creating session of type {nameof(MapPortalSession)}.");
        else if (MP.IsExecutingSyncCommandIssuedBySelf)
            session.OpenWindow();
    }

    [SyncMethod]
    public void TryAccept()
    {
        // There's not a single situation where TryAccept would return false.
        // However, it will likely be used by prefixes added by mods.
        if (PrepareDummyDialog().TryAccept())
            Remove();
    }

    [SyncMethod]
    public void Reset() => transferables.ForEach(t => t.CountToTransfer = 0);

    [SyncMethod]
    public void Remove() => Map.MpComp().sessionManager.RemoveSession(this);

    public void OpenWindow(bool sound = true)
    {
        var dialog = PrepareDummyDialog();
        if (!sound)
            dialog.soundAppear = null;

        Find.WindowStack.Add(dialog);
    }

    private MapPortalProxy PrepareDummyDialog()
    {
        return new MapPortalProxy(portal)
        {
            itemsReady = true,
            transferables = transferables,
        };
    }

    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
        Scribe_References.Look(ref portal, "portal");
    }

    public Transferable GetTransferableByThingId(int thingId)
        => transferables.Find(tr => tr.things.Any(t => t.thingIDNumber == thingId));

    public void Notify_CountChanged(Transferable tr)
    {
        // There should not really be a need to clear caches, as this dialog does not really have any.
    }

    public bool CanExistWith(Session other) => other is not MapPortalSession portalSession || portalSession.portal != portal;
}
