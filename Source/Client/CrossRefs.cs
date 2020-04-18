using HarmonyLib;
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
            {
                ScribeUtil.sharedCrossRefs.RegisterLoaded(__instance);
                ThingsById.Register(__instance);
            }
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
            ThingsById.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(PassingShipManager))]
    [HarmonyPatch(nameof(PassingShipManager.AddShip))]
    public static class ShipManagerAddPatch
    {
        static void Postfix(PassingShip vis)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(vis);
        }
    }

    [HarmonyPatch(typeof(PassingShipManager))]
    [HarmonyPatch(nameof(PassingShipManager.RemoveShip))]
    public static class ShipManagerRemovePatch
    {
        static void Postfix(PassingShip vis)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.Unregister(vis);
        }
    }

    [HarmonyPatch(typeof(PassingShipManager))]
    [HarmonyPatch(nameof(PassingShipManager.ExposeData))]
    public static class ShipManagerExposePatch
    {
        static void Postfix(PassingShipManager __instance)
        {
            if (Multiplayer.game == null) return;

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (var ship in __instance.passingShips)
                    ScribeUtil.sharedCrossRefs.RegisterLoaded(ship);
        }
    }

    [HarmonyPatch(typeof(BillStack))]
    [HarmonyPatch(nameof(BillStack.AddBill))]
    public static class BillStackAddPatch
    {
        static void Postfix(Bill bill)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(bill);
        }
    }

    [HarmonyPatch(typeof(BillStack))]
    [HarmonyPatch(nameof(BillStack.RemoveIncompletableBills))]
    public static class BillStackRemoveIncompletablePatch
    {
        static void Prefix(BillStack __instance)
        {
            if (Multiplayer.game == null) return;

            foreach (var bill in __instance.bills)
                if (!bill.CompletableEver)
                    ScribeUtil.sharedCrossRefs.Unregister(bill);
        }
    }

    [HarmonyPatch(typeof(BillStack))]
    [HarmonyPatch(nameof(BillStack.Delete))]
    public static class BillStackDeletePatch
    {
        static void Postfix(Bill bill)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.Unregister(bill);
        }
    }

    [HarmonyPatch(typeof(BillStack))]
    [HarmonyPatch(nameof(BillStack.ExposeData))]
    public static class BillStackExposePatch
    {
        static void Postfix(BillStack __instance)
        {
            if (Multiplayer.game == null) return;

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (var bill in __instance.bills)
                    ScribeUtil.sharedCrossRefs.RegisterLoaded(bill);
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
            ThingsById.UnregisterAllFrom(map);

            ScribeUtil.sharedCrossRefs.Unregister(map);
        }
    }

    [HarmonyPatch(typeof(ScribeLoader))]
    [HarmonyPatch(nameof(ScribeLoader.FinalizeLoading))]
    public static class FinalizeLoadingGame
    {
        static void Postfix()
        {
            if (Multiplayer.game == null) return;
            if (!LoadGameMarker.loading) return;

            RegisterCrossRefs();
        }

        static void RegisterCrossRefs()
        {
            ScribeUtil.sharedCrossRefs.RegisterLoaded(Find.World);

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
            if (ScribeUtil.sharedCrossRefs.allObjectsByLoadID.ContainsKey(key)) return false;

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                ScribeUtil.sharedCrossRefs.tempKeys.Add(key);

            ScribeUtil.sharedCrossRefs.allObjectsByLoadID.Add(key, reffable);

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
            ScribeUtil.sharedCrossRefs.UnregisterAllTemp();

            return false;
        }
    }
}
