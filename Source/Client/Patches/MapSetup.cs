using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.AsyncTime;
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
            // Without async time the maps tick in lockstep with the world (TickPatch.TickLockstep). A map generated
            // during the world tick still ticks in the same game tick, so it starts where the maps were before it (also
            // after DoSingleTick updated worldTicks, and with TicksGame of a nested map context). One generated later
            // starts at worldTicks and ticks from the next game tick. The other maps' clocks differ from that in the
            // middle of a game tick.
            startingMapTicks = !Multiplayer.GameComp.asyncTime && Multiplayer.AsyncWorldTime is { } worldTime
                ? AsyncWorldTimeComp.tickingWorld ? worldTime.TicksBeforeTick : worldTime.worldTicks
                : Find.Maps.Where(m => m != map).Select(m => m.AsyncTime()?.mapTicks).Max() ?? Find.TickManager.TicksGame;
            gameStartAbsTick = Find.TickManager.gameStartAbsTick;
            startingTimeSpeed = TimeSpeed.Paused;
        }

        // TickManager.CurTimeSpeed is the speed of the last context entered: a pausing letter earlier in the same world
        // tick doesn't change it, and the new map would then unpause the game
        if (!Multiplayer.GameComp.asyncTime)
            startingTimeSpeed = !usingMapTimeFromSingleplayer && Multiplayer.AsyncWorldTime is { } world
                ? world.GlobalTimeSpeed
                : Find.TickManager.CurTimeSpeed;

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
