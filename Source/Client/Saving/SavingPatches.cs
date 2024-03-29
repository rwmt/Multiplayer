﻿using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Comp;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client.Saving
{
    [HarmonyPatch(typeof(Game), nameof(Game.ExposeSmallComponents))]
    static class GameExposeComponentsPatch
    {
        static void Prefix(Game __instance)
        {
            if (Multiplayer.Client == null) return;

            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Multiplayer.game = new MultiplayerGame();

            if (Scribe.mode is LoadSaveMode.LoadingVars or LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref Multiplayer.game.gameComp, "mpGameComp");

                if (Multiplayer.game.gameComp == null)
                {
                    Log.Warning($"No {nameof(MultiplayerGameComp)} during loading/saving");
                    Multiplayer.game.gameComp = new MultiplayerGameComp();
                }
            }
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            // Convert old id blocks to vanilla unique id manager ids
            if (Scribe.mode is LoadSaveMode.LoadingVars && Multiplayer.GameComp.idBlockBase64 != null)
            {
                Log.Message("Multiplayer removing old id block...");

                var reader = new ByteReader(Convert.FromBase64String(Multiplayer.GameComp.idBlockBase64));
                var (blockStart, _, _, currentInBlock) = (reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                SetAllUniqueIds(blockStart + currentInBlock + 1);

                Multiplayer.GameComp.idBlockBase64 = null;
            }
        }

        private static void SetAllUniqueIds(int value)
        {
            typeof(UniqueIDsManager)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int))
                .Do(f => f.SetValue(Find.UniqueIDsManager, value));
        }
    }

    [HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
    static class ClearAllPatch
    {
        static void Postfix()
        {
            CacheAverageTileTemperature.Clear();
            Multiplayer.game?.OnDestroy();
            Multiplayer.game = null;
        }
    }

    [HarmonyPatch(typeof(World), nameof(World.ExposeComponents))]
    static class SaveWorldComp
    {
        static void Postfix(World __instance)
        {
            if (Multiplayer.Client == null) return;

            if (Scribe.mode is LoadSaveMode.LoadingVars or LoadSaveMode.Saving)
            {
                // Node called mpWorldComp for backwards compatibility
                Scribe_Deep.Look(ref Multiplayer.game.asyncWorldTimeComp, "mpWorldComp", __instance);

                if (Multiplayer.game.asyncWorldTimeComp == null)
                {
                    Log.Warning($"No {nameof(AsyncWorldTimeComp)} during loading/saving");
                    Multiplayer.game.asyncWorldTimeComp = new AsyncWorldTimeComp(__instance);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.ExposeComponents))]
    static class SaveMapComps
    {
        static void Postfix(Map __instance)
        {
            if (Multiplayer.Client == null) return;

            var asyncTime = __instance.AsyncTime();
            var comp = __instance.MpComp();

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref asyncTime, "mpAsyncTime", __instance);
                Scribe_Deep.Look(ref comp, "mpMapComp", __instance);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (asyncTime == null)
                {
                    Log.Error($"{typeof(AsyncTimeComp)} missing during loading");
                    // This is just so the game doesn't completely freeze
                    asyncTime = new AsyncTimeComp(__instance);
                }

                Multiplayer.game.asyncTimeComps.Add(asyncTime);

                if (comp == null)
                {
                    Log.Error($"{typeof(MultiplayerMapComp)} missing during loading");
                    comp = new MultiplayerMapComp(__instance);
                }

                Multiplayer.game.mapComps.Add(comp);
            }
        }
    }

    [HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.MapComponentTick))]
    static class MapCompTick
    {
        static void Postfix(Map map)
        {
            if (Multiplayer.Client == null) return;
            map.MpComp()?.DoTick();
        }
    }

    [HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.FinalizeInit))]
    static class MapCompFinalizeInit
    {
        static void Postfix(Map map)
        {
            if (Multiplayer.Client == null) return;
            map.AsyncTime()?.FinalizeInit();
        }
    }

    [HarmonyPatch(typeof(WorldComponentUtility), nameof(WorldComponentUtility.FinalizeInit))]
    static class WorldCompFinalizeInit
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.AsyncWorldTime.FinalizeInit();
        }
    }

    [HarmonyPatch(typeof(LoadedObjectDirectory), nameof(LoadedObjectDirectory.RegisterLoaded))]
    static class FixRegisterLoaded
    {
        static bool Prefix(LoadedObjectDirectory __instance, ref ILoadReferenceable reffable)
        {
            string text = "[excepted]";
            try
            {
                text = reffable.GetUniqueLoadID();
            }
            catch
            {
            }

            return !__instance.allObjectsByLoadID.ContainsKey(text);
        }
    }
}
