using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
public static class MapSetup
{
    public static bool SetupNextMapFromTickZero;

    static void Prefix(ref Action<Map> extraInitBeforeContentGen)
    {
        if (Multiplayer.Client == null) return;
        extraInitBeforeContentGen += map => SetupMap(map);
    }

    public static void SetupMap(Map map, bool usingMapTimeFromSingleplayer = false)
    {
        Log.Message("MP: Setting up map " + map.uniqueID);

        // Initialize and store Multiplayer

        var mapComp = new MultiplayerMapComp(map);
        Multiplayer.game.mapComps.Add(mapComp);

        var async = CreateAsyncTimeCompForMap(map, usingMapTimeFromSingleplayer);
        Multiplayer.game.asyncTimeComps.Add(async);

        // Ask the world, not the global TickManager: the global is viewer-dependent
        // and this value is scribed onto the new map. Not on the singleplayer-
        // conversion path (no client exists there; the world would read Paused and
        // stomp the singleplayer speed). Runs after the comp is registered: the world
        // getter walks every map's comp, and reading it while this map is comp-less
        // is what let an exception here abort SetupMap and leave the map comp-less
        // forever. The fresh comp sits at Paused, which the running-map filter
        // ignores, so the value is unchanged.
        if (!usingMapTimeFromSingleplayer && !Multiplayer.GameComp.asyncTime)
            async.DesiredTimeSpeed = Multiplayer.AsyncWorldTime?.DesiredTimeSpeed ?? Find.TickManager.CurTimeSpeed;

        // Store all current managers for Faction.OfPlayer
        InitFactionDataFromMap(map, Faction.OfPlayer);

        // Add all other (non Faction.OfPlayer) factions to the map
        foreach (var faction in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
            if (faction != Faction.OfPlayer)
                InitNewFactionData(map, faction);
    }

    private static AsyncTimeComp CreateAsyncTimeCompForMap(Map map, bool usingMapTimeFromSingleplayer)
    {
        int startingMapTicks;
        int gameStartAbsTick;
        TimeSpeed startingTimeSpeed;

        bool startingMapTimeFromBeginning =
            Multiplayer.GameComp.multifaction &&
            Multiplayer.GameComp.asyncTime &&
            SetupNextMapFromTickZero;

        if (usingMapTimeFromSingleplayer)
        {
            startingMapTicks = Find.TickManager.TicksGame;
            gameStartAbsTick = Find.TickManager.gameStartAbsTick;
            startingTimeSpeed = Find.TickManager.CurTimeSpeed;
        }
        else if (startingMapTimeFromBeginning)
        {
            startingMapTicks = 0;
            gameStartAbsTick = GenTicks.ConfiguredTicksAbsAtGameStart;
            startingTimeSpeed = TimeSpeed.Paused;
        }
        else
        {
            startingMapTicks = Find.Maps.Where(m => m != map).Select(m => m.AsyncTime()?.mapTicks).Max() ?? Find.TickManager.TicksGame;
            gameStartAbsTick = Find.TickManager.gameStartAbsTick;
            startingTimeSpeed = TimeSpeed.Paused;
        }

        // The non-async world-speed override happens in SetupMap AFTER this comp is
        // registered - see the comment there.
        var asyncTimeCompForMap = new AsyncTimeComp(map, gameStartAbsTick)
        {
            mapTicks = startingMapTicks,
            DesiredTimeSpeed = startingTimeSpeed
        };

        SetupNextMapFromTickZero = false;

        return asyncTimeCompForMap;
    }

    private static void InitFactionDataFromMap(Map map, Faction f)
    {
        var mapComp = map.MpComp();
        mapComp.factionData[f.loadID] = FactionMapData.NewFromMap(map, f.loadID);

        var customData = mapComp.customFactionData[f.loadID] = CustomFactionMapData.New(f.loadID, map);

        foreach (var t in map.listerThings.AllThings)
            if (t is ThingWithComps tc &&
                tc.GetComp<CompForbiddable>() is { forbiddenInt: false })
                customData.unforbidden.Add(t);
    }

    public static void InitNewFactionData(Map map, Faction f)
    {
        var mapComp = map.MpComp();

        mapComp.factionData[f.loadID] = FactionMapData.New(f.loadID, map);
        mapComp.factionData[f.loadID].areaManager.AddStartingAreas();

        mapComp.customFactionData[f.loadID] = CustomFactionMapData.New(f.loadID, map);
    }
}
