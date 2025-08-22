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
        PatchTickmanagerPlayerCanControlGetter.ResetLandingMessageFlag();
    }
}

public static class GravshipTravelUtils
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

    public static void StartFreeze()
    {
        SetFreeze(true);
    }

    public static void StopFreeze()
    {
        SetFreeze(false);
    }

    private static void SetFreeze(bool value)
    { 
    
        Multiplayer.Client.Send(Common.Packets.Client_Freeze, [value]);
    }
    private static string GravshipDialogPrefix => "ConfirmGravEngineLaunch".Translate().RawText;

    // TODO: Try to find a better solution for that
    public static void CloseGravshipDialog()
    {
        Dialog_MessageBox dialog = Find.WindowStack.Windows
            .OfType<Dialog_MessageBox>()
            .FirstOrDefault(w => w.text.RawText.StartsWith(GravshipDialogPrefix));
        dialog?.Close();
    }
    public static bool IsGravShipMessageDialog(Dialog_MessageBox messageBox)
    {
        return messageBox.text.RawText.StartsWith(GravshipDialogPrefix);
    }

    [SyncMethod]
    public static void SyncGravshipDialogCancel() => CloseGravshipDialog();

    [SyncMethod]
    public static void SyncCloseSession(PlanetTile tile) => CloseSessionAt(tile);
}
