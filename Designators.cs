using Harmony;
using Harmony.ILCopying;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Verse;

namespace Multiplayer
{
    [HarmonyPatch(typeof(DesignatorManager))]
    [HarmonyPatch(nameof(DesignatorManager.ProcessInputEvents))]
    public static class ProcessDesigInputPatch
    {
        public static bool processing;

        static void Prefix()
        {
            processing = true;
        }

        static void Postfix()
        {
            processing = false;
        }
    }

    [HarmonyPatch(typeof(Designator))]
    [HarmonyPatch(nameof(Designator.Finalize))]
    [HarmonyPatch(new Type[] { typeof(bool) })]
    public static class DesignatorFinalizePatch
    {
        static bool Prefix(bool somethingSucceeded)
        {
            if (Multiplayer.client == null) return true;
            return !somethingSucceeded;
        }
    }

    public static class DesignatorPatches
    {
        public static bool DesignateSingleCell(Designator designator, IntVec3 c)
        {
            if (Multiplayer.client == null || !ProcessDesigInputPatch.processing) return true;

            Map map = Find.VisibleMap;
            object[] extra = GetExtra(0, designator).Append(map.cellIndices.CellToIndex(c));
            Multiplayer.client.SendAction(ServerAction.DESIGNATOR, extra);

            return false;
        }

        public static bool DesignateMultiCell(Designator designator, IEnumerable<IntVec3> cells)
        {
            if (Multiplayer.client == null || !ProcessDesigInputPatch.processing) return true;

            Map map = Find.VisibleMap;
            int[] cellData = new int[cells.Count()];
            int i = 0;
            foreach (IntVec3 cell in cells)
                cellData[i++] = map.cellIndices.CellToIndex(cell);

            object[] extra = GetExtra(1, designator).Append(cellData);
            Multiplayer.client.SendAction(ServerAction.DESIGNATOR, extra);

            return false;
        }

        public static bool DesignateThing(Designator designator, Thing t)
        {
            if (Multiplayer.client == null || !DrawGizmosPatch.drawingGizmos) return true;

            object[] extra = GetExtra(2, designator).Append(t.GetUniqueLoadID());
            Multiplayer.client.SendAction(ServerAction.DESIGNATOR, extra);

            return false;
        }

        private static object[] GetExtra(int action, Designator designator)
        {
            string buildDefName = designator is Designator_Build build ? build.PlacingDef.defName : "";
            return new object[] { action, designator.GetType().FullName, buildDefName, Find.VisibleMap.GetUniqueLoadID(), Multiplayer.RealPlayerFaction.GetUniqueLoadID() }.Append(Metadata(designator));
        }

        public static IEnumerable<CodeInstruction> DesignateSingleCell_Transpiler(MethodBase method, ILGenerator gen, IEnumerable<CodeInstruction> code)
        {
            return Multiplayer.PrefixTranspiler(method, gen, code, typeof(DesignatorPatches).GetMethod("DesignateSingleCell"));
        }

        public static IEnumerable<CodeInstruction> DesignateMultiCell_Transpiler(MethodBase method, ILGenerator gen, IEnumerable<CodeInstruction> code)
        {
            return Multiplayer.PrefixTranspiler(method, gen, code, typeof(DesignatorPatches).GetMethod("DesignateMultiCell"));
        }

        public static IEnumerable<CodeInstruction> DesignateThing_Transpiler(MethodBase method, ILGenerator gen, IEnumerable<CodeInstruction> code)
        {
            return Multiplayer.PrefixTranspiler(method, gen, code, typeof(DesignatorPatches).GetMethod("DesignateThing"));
        }

        public static readonly FieldInfo selectedAreaField = typeof(Designator_AreaAllowed).GetField("selectedArea", BindingFlags.Static | BindingFlags.NonPublic);
        public static readonly FieldInfo buildStuffField = typeof(Designator_Build).GetField("stuffDef", BindingFlags.Instance | BindingFlags.NonPublic);
        public static readonly FieldInfo buildRotField = typeof(Designator_Place).GetField("placingRot", BindingFlags.Instance | BindingFlags.NonPublic);

        private static object[] Metadata(Designator designator)
        {
            List<object> meta = new List<object>();

            if (designator is Designator_AreaAllowed)
            {
                Area selectedArea = (Area)selectedAreaField.GetValue(null);
                meta.Add(selectedArea != null ? selectedArea.GetUniqueLoadID() : "");
            }

            if (designator is Designator_Place)
            {
                meta.Add(((Rot4)buildRotField.GetValue(designator)).AsByte);
            }

            if (designator is Designator_Build build && build.PlacingDef.MadeFromStuff)
            {
                meta.Add(((ThingDef)buildStuffField.GetValue(designator)).defName);
            }

            if (designator is Designator_Install)
            {
                meta.Add(ThingToInstall().GetUniqueLoadID());
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
    [HarmonyPatch("MiniToInstallOrBuildingToReinstall", PropertyMethod.Getter)]
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
    [HarmonyPatch(nameof(Game.VisibleMap), PropertyMethod.Getter)]
    public static class VisibleMapGetPatch
    {
        public static Map visibleMap;

        static void Postfix(ref Map __result)
        {
            if (visibleMap != null)
                __result = visibleMap;
        }
    }

    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch(nameof(Game.VisibleMap), PropertyMethod.Setter)]
    public static class VisibleMapSetPatch
    {
        public static bool ignore;

        static bool Prefix()
        {
            return !ignore;
        }
    }
}
