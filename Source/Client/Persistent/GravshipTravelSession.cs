using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

// World-level session: always registered with WorldComp, never pauses a map
public class GravshipTravelSession : ExposableSession
{
    private Map map;

    public override Map Map => map;
    public PlanetTile InitialTile;

    public GravshipTravelSession(Map map) : base(map)
    {
        if (map == null)
        {
            MpLog.Error("[MP] GravshipTravelSession: Cannot create session with null map.");
            return;
        }

        MpLog.Debug($"[MP] GravshipTravelSession: Creating session");
        InitialTile = map.Tile;
        RegisterMap(map);
    }

    public override bool IsCurrentlyPausing(Map map) => Map != null && map == Map;
    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry) => null;

    public void RegisterMap(Map map)
    {
        MpLog.Debug($"[MP] GravshipTravelSession: Registering map");
        UnregisterMap();
        map.MpComp()?.sessionManager?.AddSession(this);

        this.map = map;
    }

    public void UnregisterMap()
    {
        if (map == null) return;

        MpLog.Debug($"[MP] GravshipTravelSession: Unregistering map");
        map.MpComp()?.sessionManager?.RemoveSession(this);
        map = null;
    }

    public override void PostRemoveSession()
    {
        TickManager_PlayerCanControl_Patch.ResetLandingMessageFlag();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref map, "map", false);
        Scribe_Values.Look(ref InitialTile, "initialTile");
    }
}

public static class GravshipTravelSessionUtils
{
    public static void CreateGravshipTravelSession(Map map)
    {
        GravshipTravelSession session = new(map);
        Multiplayer.Client.Multiplayer.WorldComp.sessionManager.AddSession(session);
    }

    public static GravshipTravelSession GetSession(PlanetTile takeoffTile) =>
        Multiplayer.Client.Multiplayer.WorldComp.sessionManager.AllSessions.OfType<GravshipTravelSession>().FirstOrDefault(s => s.InitialTile == takeoffTile);

    [SyncMethod]
    public static void RegisterMap(PlanetTile tile, Map map) => GetSession(tile)?.RegisterMap(map);

    [SyncMethod]
    public static void UnregisterMap(PlanetTile takeoffTile) => GetSession(takeoffTile)?.UnregisterMap();

    [SyncMethod]
    public static void SyncCloseSession(PlanetTile tile) => CloseSession(tile);

    public static void CloseSession(PlanetTile tile)
    {
        GravshipTravelSession session = GetSession(tile);
        if (session == null) return;

        MpLog.Debug($"[MP] GravshipTravelSession: Closing session for tile {tile}");
        session.UnregisterMap();
        Multiplayer.Client.Multiplayer.WorldComp.sessionManager.RemoveSession(session);
    }
}
