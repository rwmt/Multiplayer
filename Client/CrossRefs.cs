using Harmony;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.SpawnSetup))]
    public static class ThingSpawnPatch
    {
        static void Postfix(Thing __instance)
        {
            if (Multiplayer.game == null) return;

            if (__instance.def.HasThingIDNumber)
                ScribeUtil.sharedCrossRefs.RegisterLoaded(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.DeSpawn))]
    public static class ThingDeSpawnPatch
    {
        static void Postfix(Thing __instance)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.SpawnSetup))]
    public static class WorldObjectSpawnPatch
    {
        static void Postfix(WorldObject __instance)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.PostRemove))]
    public static class WorldObjectRemovePatch
    {
        static void Postfix(WorldObject __instance)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(FactionManager))]
    [HarmonyPatch(nameof(FactionManager.Add))]
    public static class FactionAddPatch
    {
        static void Postfix(Faction faction)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(faction);
        }
    }

    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch(nameof(Game.AddMap))]
    public static class AddMapPatch
    {
        static void Postfix(Map map)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(map);
        }
    }

    [HarmonyPatch(typeof(MapDeiniter))]
    [HarmonyPatch(nameof(MapDeiniter.Deinit))]
    public static class DeinitMapPatch
    {
        static void Prefix(Map map)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.UnregisterAllFrom(map);
            ScribeUtil.sharedCrossRefs.Unregister(map);
        }
    }

    [HarmonyPatch(typeof(ScribeLoader))]
    [HarmonyPatch(nameof(ScribeLoader.FinalizeLoading))]
    public static class FinalizeLoadingGame
    {
        static void Postfix()
        {
            if (Multiplayer.game == null || !LoadGamePatch.loading) return;
            RegisterCrossRefs();
        }

        static void RegisterCrossRefs()
        {
            foreach (Faction f in Find.FactionManager.AllFactions)
                ScribeUtil.sharedCrossRefs.RegisterLoaded(f);

            foreach (Map map in Find.Maps)
                ScribeUtil.sharedCrossRefs.RegisterLoaded(map);
        }
    }

    [HarmonyPatch(typeof(LoadedObjectDirectory))]
    [HarmonyPatch(nameof(LoadedObjectDirectory.RegisterLoaded))]
    public static class LoadedObjectsRegisterPatch
    {
        static bool Prefix(LoadedObjectDirectory __instance, ILoadReferenceable reffable)
        {
            if (!(__instance is SharedCrossRefs)) return true;
            if (reffable == null) return false;

            string key = reffable.GetUniqueLoadID();
            if (ScribeUtil.sharedCrossRefs.Dict.ContainsKey(key)) return false;

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                ScribeUtil.sharedCrossRefs.tempKeys.Add(key);

            ScribeUtil.sharedCrossRefs.Dict.Add(key, reffable);

            return false;
        }
    }

    [HarmonyPatch(typeof(LoadedObjectDirectory))]
    [HarmonyPatch(nameof(LoadedObjectDirectory.Clear))]
    public static class LoadedObjectsClearPatch
    {
        static bool Prefix(LoadedObjectDirectory __instance)
        {
            if (!(__instance is SharedCrossRefs)) return true;

            Scribe.loader.crossRefs.loadedObjectDirectory = ScribeUtil.defaultCrossRefs;

            foreach (string temp in ScribeUtil.sharedCrossRefs.tempKeys)
                ScribeUtil.sharedCrossRefs.Unregister(temp);
            ScribeUtil.sharedCrossRefs.tempKeys.Clear();

            return false;
        }
    }
}
