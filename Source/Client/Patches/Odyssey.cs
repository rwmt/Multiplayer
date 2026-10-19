using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(FishShadowComponent), nameof(FishShadowComponent.MapComponentTick))]
    public static class FishShadowComponent_MapComponentTick_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo objectHash = AccessTools.Method(typeof(object), nameof(object.GetHashCode));
            MethodInfo waterBodyHash = AccessTools.Method(typeof(FishShadowComponent_MapComponentTick_Patch), nameof(FishShadowComponent_MapComponentTick_Patch.WaterBodyHash));

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(objectHash))
                    yield return new CodeInstruction(OpCodes.Call, waterBodyHash);
                else
                    yield return instruction;
            }
        }

        public static int WaterBodyHash(WaterBody body)
        {
            if (Multiplayer.Client == null)
                return body.GetHashCode();

            return Gen.HashCombineInt(body.map?.uniqueID ?? 0, body.rootCell.x, body.rootCell.z, (int)body.waterBodyType);
        }
    }


    // Suppress auto-popup of Dialog_NamePlayerGravship in MP.
    // Manual rename via inspect tab (IRenameable) is already synced by SyncMethods.
    [HarmonyPatch(typeof(Building_GravEngine), nameof(Building_GravEngine.UpdateSubstructureIfNeeded))]
    public static class SuppressGravshipNamingDialog
    {
        static void Prefix(ref bool ___haveShownNameDialog)
        {
            if (Multiplayer.Client == null) return;

            ___haveShownNameDialog = true;
        }
    }

    // Vanilla caches each map's grav engine for the current TicksGame (GravshipUtility.GetPlayerGravEngine_NewTemp,
    // behind Map.IsPlayerHome). The interface calls it too, at the current map's time: the same TicksGame as the
    // commands executed before that map's next tick and its MapPreTick. A value the interface cached before a command
    // changed the map would then be used by the simulation on some clients only. Every tick and command starts with an
    // empty cache instead, so its content depends only on the simulation.
    public static class GravEngineCache
    {
        private static readonly Dictionary<(Map, Faction), Building_GravEngine> factionEngines = new();
        private static int factionEnginesTick = -1;
        private static readonly List<Thing> tmpThings = new();

        public static void Reset()
        {
            GravshipUtility.tmpMapToGravship.Clear();
            GravshipUtility.lastCachedEngineTick = -1;

            factionEngines.Clear();
            factionEnginesTick = -1;
        }

        public static bool OfOtherPlayer(Thing engine, Faction faction) =>
            engine.Faction is { IsPlayer: true } f && f != faction;

        // Cached like vanilla's lookup
        public static Building_GravEngine GetEngineOf(Map map, Faction faction)
        {
            if (Find.TickManager.TicksGame != factionEnginesTick)
            {
                factionEngines.Clear();
                factionEnginesTick = Find.TickManager.TicksGame;
            }

            if (!factionEngines.TryGetValue((map, faction), out var engine))
                factionEngines[(map, faction)] = engine = FindEngine(map, faction);

            return engine;
        }

        // Vanilla's search order: spawned engines, spawned minified ones, then minified ones held in containers
        private static Building_GravEngine FindEngine(Map map, Faction faction)
        {
            var def = ThingDefOf.GravEngine;

            foreach (var t in map.listerThings.ThingsOfDef(def))
                if (t is Building_GravEngine e && !OfOtherPlayer(e, faction))
                    return e;

            foreach (var t in map.listerThings.ThingsOfDef(def.minifiedDef))
                if (t.GetInnerIfMinified() is Building_GravEngine e && !OfOtherPlayer(e, faction))
                    return e;

            try
            {
                ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForDef(def.minifiedDef), tmpThings,
                    allowUnreal: true, null, alsoGetSpawnedThings: false);

                foreach (var t in tmpThings)
                    if (t.GetInnerIfMinified() is Building_GravEngine e && !OfOtherPlayer(e, faction))
                        return e;
            }
            finally
            {
                tmpThings.Clear();
            }

            return null;
        }
    }

    // Multifaction: vanilla takes the first grav engine on the map, which can be another player's (making the map a
    // home of every player faction, see Map.IsPlayerHome). Under a player faction's context look for one that isn't
    // another player faction's (unowned engines count, as in vanilla). The spectator context (world ticks, maps of no
    // player, see Map_IsPlayerHome_Spectator_Patch) keeps vanilla's result.
    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.GetPlayerGravEngine_NewTemp))]
    static class GetPlayerGravEngineFactionPatch
    {
        static void Postfix(Map map, ref Building_GravEngine __result)
        {
            if (__result == null || Multiplayer.Client == null || !Multiplayer.GameComp.multifaction) return;

            var faction = Faction.OfPlayer;
            if (faction == null || faction == Multiplayer.WorldComp.spectatorFaction ||
                !GravEngineCache.OfOtherPlayer(__result, faction))
                return;

            __result = GravEngineCache.GetEngineOf(map, faction);
        }
    }

    [HarmonyPatch(typeof(CompStatue), nameof(CompStatue.InitFakePawn))]
    public static class PatchInitFakePawnToNotSyncPawnName
    {
        static void Prefix(ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            if(!Multiplayer.dontSync)
            {
                Multiplayer.dontSync = true;
                __state = true;
            }
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Multiplayer.dontSync = false;
        }
    }
}
