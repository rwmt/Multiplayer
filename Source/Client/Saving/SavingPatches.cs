using HarmonyLib;
using Multiplayer.Client.Comp;
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
                Scribe_Deep.Look(ref Multiplayer.game.gameComp, "mpGameComp", __instance);

                if (Multiplayer.game.gameComp == null)
                {
                    Log.Warning($"No {nameof(MultiplayerGameComp)} during loading/saving");
                    Multiplayer.game.gameComp = new MultiplayerGameComp(__instance);
                }
            }
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
                Scribe_Deep.Look(ref Multiplayer.game.worldComp, "mpWorldComp", __instance);

                if (Multiplayer.game.worldComp == null)
                {
                    Log.Warning($"No {nameof(MultiplayerWorldComp)} during loading/saving");
                    Multiplayer.game.worldComp = new MultiplayerWorldComp(__instance);
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
            Multiplayer.WorldComp.FinalizeInit();
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
