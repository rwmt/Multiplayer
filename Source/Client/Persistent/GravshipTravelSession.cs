using Multiplayer.API;
using Multiplayer.Client.Patches;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
using Verse;

namespace Multiplayer.Client.Persistent;

public class GravshipTravelSession : Session
{
    public override Map Map => map;
    public PlanetTile InitialTile;

    private Map map;

    public GravshipTravelSession(Map map) : base(map)
    {
        this.map = map;
        InitialTile = map.Tile;
    }

    public override bool IsCurrentlyPausing(Map map) => Map != null && map == Map;
    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry) => null;

    public void StopPausing()
    {
        map = null;
    }

    public override void PostRemoveSession()
    {
        TickManager_PlayerCanControl_Patch.ResetLandingMessageFlag();
    }
}

public static class GravshipTravelSessionUtils
{
    public static void OpenSessionAt(PlanetTile tile)
    {
        Map map = Find.WorldObjects.MapParentAt(tile).Map;

        if (HasSessionAt(map.Tile))
            return;

        GravshipTravelSession session = new GravshipTravelSession(map);
        map.MpComp()?.sessionManager?.AddSession(session);
    }  

    public static void CloseSessionAt(PlanetTile tile)
    {
        if(TryGetSessionAt(tile, out GravshipTravelSession session))
        {
            session.Map.MpComp()?.sessionManager?.RemoveSession(session);
            session.StopPausing();
        }
    }

    public static bool TryGetSessionAt(PlanetTile takeoffTile, out GravshipTravelSession session)
    {
        session = null;

        foreach (var sessionManager in Multiplayer.game.mapComps.Select(mp => mp.sessionManager))
        {
            foreach (var iSession in sessionManager.AllSessions.OfType<GravshipTravelSession>())
            {
                if (iSession.InitialTile == takeoffTile)
                {
                    session = iSession;
                    return true;
                }
                    
            }
        }

        return false;
    }

    public static bool HasSessionAt(PlanetTile takeoffTile)
    {
        return TryGetSessionAt(takeoffTile, out _);
    }

    [SyncMethod]
    public static void SyncCloseSession(PlanetTile tile) => CloseSessionAt(tile);
}
