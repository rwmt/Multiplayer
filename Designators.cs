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

            string desName = designator.GetType().FullName;
            Map map = Find.VisibleMap;
            byte[] extra = Server.GetBytes(0, desName, map.GetUniqueLoadID(), Multiplayer.RealPlayerFaction.GetUniqueLoadID(), map.cellIndices.CellToIndex(c));
            Multiplayer.client.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { ServerAction.DESIGNATOR, extra });

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

            string desName = designator.GetType().FullName;
            byte[] extra = Server.GetBytes(1, desName, map.GetUniqueLoadID(), Multiplayer.RealPlayerFaction.GetUniqueLoadID(), cellData);
            Multiplayer.client.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { ServerAction.DESIGNATOR, extra });

            return false;
        }

        public static bool DesignateThing(Designator designator, Thing t)
        {
            if (Multiplayer.client == null || !DrawGizmosPatch.drawingGizmos) return true;

            Log.Message("designate thing " + t.GetUniqueLoadID());

            string desName = designator.GetType().FullName;
            byte[] extra = Server.GetBytes(2, desName, Find.VisibleMap.GetUniqueLoadID(), Multiplayer.RealPlayerFaction.GetUniqueLoadID(), t.GetUniqueLoadID());
            Multiplayer.client.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { ServerAction.DESIGNATOR, extra });

            return false;
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
    }
}
