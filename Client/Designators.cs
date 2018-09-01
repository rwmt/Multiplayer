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
    [HarmonyPatch(new Type[] { typeof(bool) })]
    public static class DesignatorFinalizePatch
    {
        static bool Prefix(bool somethingSucceeded)
        {
            if (Multiplayer.client == null) return true;
            return somethingSucceeded;
        }
    }

    public static class DesignatorPatches
    {
        [IndexedPatchParameters]
        public static bool DesignateSingleCell(Designator designator, IntVec3 cell)
        {
            if (!Multiplayer.ShouldSync) return true;

            Map map = Find.CurrentMap;
            object[] extra = GetExtra(0, designator).Append(map.cellIndices.CellToIndex(cell));
            Multiplayer.client.SendCommand(CommandType.DESIGNATOR, map.uniqueID, extra);

            return false;
        }

        [IndexedPatchParameters]
        public static bool DesignateMultiCell(Designator designator, IEnumerable<IntVec3> cells)
        {
            if (!Multiplayer.ShouldSync) return true;

            Map map = Find.CurrentMap;
            int[] cellData = new int[cells.Count()];
            int i = 0;
            foreach (IntVec3 cell in cells)
                cellData[i++] = map.cellIndices.CellToIndex(cell);

            object[] extra = GetExtra(1, designator).Append(cellData);
            Multiplayer.client.SendCommand(CommandType.DESIGNATOR, map.uniqueID, extra);

            return false;
        }

        [IndexedPatchParameters]
        public static bool DesignateThing(Designator designator, Thing thing)
        {
            if (!Multiplayer.ShouldSync) return true;

            Map map = Find.CurrentMap;
            object[] extra = GetExtra(2, designator).Append(thing.thingIDNumber);
            Multiplayer.client.SendCommand(CommandType.DESIGNATOR, map.uniqueID, extra);

            MoteMaker.ThrowMetaPuffs(thing);

            return false;
        }

        private static object[] GetExtra(int action, Designator designator)
        {
            string buildDefName = designator is Designator_Build build ? build.PlacingDef.defName : "";
            return new object[] { action, designator.GetType().FullName, buildDefName }.Append(Metadata(designator));
        }

        private static object[] Metadata(Designator designator)
        {
            List<object> meta = new List<object>();

            if (designator is Designator_AreaAllowed)
            {
                meta.Add(Designator_AreaAllowed.SelectedArea != null ? Designator_AreaAllowed.SelectedArea.ID : -1);
            }

            if (designator is Designator_Place place)
            {
                meta.Add(place.placingRot.AsByte);
            }

            if (designator is Designator_Build build && build.PlacingDef.MadeFromStuff)
            {
                meta.Add(build.stuffDef.defName);
            }

            if (designator is Designator_Install)
            {
                meta.Add(ThingToInstall().thingIDNumber);
            }

            return meta.ToArray();
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
        public static Map visibleMap;

        static void Postfix(ref Map __result)
        {
            if (visibleMap != null)
                __result = visibleMap;
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
