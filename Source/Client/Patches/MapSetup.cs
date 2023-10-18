using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
public static class MapSetup
{
    static void Prefix(ref Action<Map> extraInitBeforeContentGen)
    {
        if (Multiplayer.Client == null) return;
        extraInitBeforeContentGen += SetupMap;
    }

    static void Postfix()
    {
        if (Multiplayer.Client == null) return;

        Log.Message("Rand " + Rand.StateCompressed);
    }

    public static void SetupMap(Map map)
    {
        Log.Message("New map " + map.uniqueID);
        Log.Message("Rand " + Rand.StateCompressed);

        // Initialize and store Multiplayer components
        var async = new AsyncTimeComp(map);
        Multiplayer.game.asyncTimeComps.Add(async);

        var mapComp = new MultiplayerMapComp(map);
        Multiplayer.game.mapComps.Add(mapComp);

        // Store all current managers for Faction.OfPlayer
        InitFactionDataFromMap(map, Faction.OfPlayer);

        // Add all other (non Faction.OfPlayer) factions to the map
        foreach (var faction in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
            if (faction != Faction.OfPlayer)
                InitNewFactionData(map, faction);

        async.mapTicks = Find.Maps.Where(m => m != map).Select(m => m.AsyncTime()?.mapTicks).Max() ?? Find.TickManager.TicksGame;
        async.storyteller = new Storyteller(Find.Storyteller.def, Find.Storyteller.difficultyDef, Find.Storyteller.difficulty);
        async.storyWatcher = new StoryWatcher();

        if (!Multiplayer.GameComp.asyncTime)
            async.SetDesiredTimeSpeed(Find.TickManager.CurTimeSpeed);
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
