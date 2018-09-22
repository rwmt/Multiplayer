using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Designator))]
    [HarmonyPatch(nameof(Designator.Finalize))]
    [HarmonyPatch(new[] { typeof(bool) })]
    public static class DesignatorFinalizePatch
    {
        static bool Prefix(bool somethingSucceeded)
        {
            if (Multiplayer.Client == null) return true;
            return !somethingSucceeded || Multiplayer.ExecutingCmds;
        }
    }

    public static class DesignatorPatches
    {
        [IndexedPatchParameters]
        public static bool DesignateSingleCell(Designator designator, IntVec3 cell)
        {
            if (!Multiplayer.ShouldSync) return true;

            Map map = Find.CurrentMap;
            ByteWriter data = new ByteWriter();
            WriteData(data, 0, designator);
            Sync.WriteSync(data, cell);
            Multiplayer.Client.SendCommand(CommandType.DESIGNATOR, map.uniqueID, data.GetArray());

            return false;
        }

        [IndexedPatchParameters]
        public static bool DesignateMultiCell(Designator designator, IEnumerable<IntVec3> cells)
        {
            if (!Multiplayer.ShouldSync) return true;
            if (cells.Count() == 0) return true; // No cells implies Finalize(false), which currently doesn't cause side effects

            Map map = Find.CurrentMap;
            ByteWriter data = new ByteWriter();
            WriteData(data, 1, designator);

            IntVec3[] cellArray = cells.ToArray();
            Sync.WriteSync(data, cellArray);

            Multiplayer.Client.SendCommand(CommandType.DESIGNATOR, map.uniqueID, data.GetArray());

            return false;
        }

        [IndexedPatchParameters]
        public static bool DesignateThing(Designator designator, Thing thing)
        {
            if (!Multiplayer.ShouldSync) return true;

            Map map = Find.CurrentMap;
            ByteWriter data = new ByteWriter();
            WriteData(data, 2, designator);
            Sync.WriteSync(data, thing);
            Multiplayer.Client.SendCommand(CommandType.DESIGNATOR, map.uniqueID, data.GetArray());

            MoteMaker.ThrowMetaPuffs(thing);

            return false;
        }

        private static void WriteData(ByteWriter data, int action, Designator designator)
        {
            data.WriteInt32(action);
            Sync.WriteSync(data, designator);

            if (designator is Designator_AreaAllowed)
                Sync.WriteSync(data, Designator_AreaAllowed.SelectedArea);

            if (designator is Designator_Place place)
                Sync.WriteSync(data, place.placingRot);

            if (designator is Designator_Build build && build.PlacingDef.MadeFromStuff)
                Sync.WriteSync(data, build.stuffDef);

            if (designator is Designator_Install)
                Sync.WriteSync(data, ThingToInstall().thingIDNumber);
        }

        private static Thing ThingToInstall()
        {
            Thing singleSelectedThing = Find.Selector.SingleSelectedThing;
            if (singleSelectedThing is MinifiedThing)
                return singleSelectedThing;

            Building building = singleSelectedThing as Building;
            if (building != null && building.def.Minifiable)
                return singleSelectedThing;

            return null;
        }
    }

    [HarmonyPatch(typeof(Designator_Install))]
    [HarmonyPatch(nameof(Designator_Install.MiniToInstallOrBuildingToReinstall), PropertyMethod.Getter)]
    public static class DesignatorInstallPatch
    {
        public static Thing thingToInstall;

        static void Postfix(ref Thing __result)
        {
            if (thingToInstall != null)
                __result = thingToInstall;
        }
    }

    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch(nameof(Game.CurrentMap), PropertyMethod.Getter)]
    public static class CurrentMapGetPatch
    {
        public static Map currentMap;

        static void Postfix(ref Map __result)
        {
            if (currentMap != null)
                __result = currentMap;
        }
    }

    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch(nameof(Game.CurrentMap), PropertyMethod.Setter)]
    public static class CurrentMapSetPatch
    {
        public static bool ignore;

        static bool Prefix()
        {
            return !ignore;
        }
    }
}
