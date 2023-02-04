using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncUtil
    {
        public static bool isDialogNodeTreeOpen = false;

        internal static void DialogNodeTreePostfix()
        {
            if (Multiplayer.Client != null && Find.WindowStack?.WindowOfType<Dialog_NodeTree>() != null)
                isDialogNodeTreeOpen = true;
        }

        internal static void PatchMethodForDialogNodeTreeSync(MethodBase method)
        {
            Multiplayer.harmony.PatchMeasure(method, postfix: new HarmonyMethod(typeof(SyncUtil), nameof(SyncUtil.DialogNodeTreePostfix)));
        }

        internal static void PatchMethodForSync(MethodBase method)
        {
            Multiplayer.harmony.PatchMeasure(method, transpiler: SyncTemplates.CreateTranspiler());
        }

        public static SyncHandler HandleCmd(ByteReader data)
        {
            int syncId = data.ReadInt32();
            SyncHandler handler;

            try
            {
                handler = Sync.handlers[syncId];
            }
            catch (ArgumentOutOfRangeException)
            {
                Log.Error($"Error: invalid syncId {syncId}/{Sync.handlers.Count}, this implies mismatched mods, ensure your versions match! Stacktrace follows.");
                throw;
            }

            List<object> prevSelected = Find.Selector.selected;
            List<WorldObject> prevWorldSelected = Find.WorldSelector.selected;

            bool shouldQueue = false;

            if (handler.context != SyncContext.None)
            {
                if (handler.context.HasFlag(SyncContext.MapMouseCell))
                {
                    IntVec3 mouseCell = SyncSerialization.ReadSync<IntVec3>(data);
                    MouseCellPatch.result = mouseCell;
                }

                if (handler.context.HasFlag(SyncContext.MapSelected))
                {
                    List<ISelectable> selected = SyncSerialization.ReadSync<List<ISelectable>>(data);
                    Find.Selector.selected = selected.Cast<object>().AllNotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.WorldSelected))
                {
                    List<ISelectable> selected = SyncSerialization.ReadSync<List<ISelectable>>(data);
                    Find.WorldSelector.selected = selected.Cast<WorldObject>().AllNotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.QueueOrder_Down))
                    shouldQueue = data.ReadBool();
            }

            KeyIsDownPatch.shouldQueue = shouldQueue;

            try
            {
                handler.Handle(data);
            }
            finally
            {
                MouseCellPatch.result = null;
                KeyIsDownPatch.shouldQueue = null;
                Find.Selector.selected = prevSelected;
                Find.WorldSelector.selected = prevWorldSelected;
            }

            return handler;
        }

        public static void WriteContext(SyncHandler handler, ByteWriter data)
        {
            if (handler.context == SyncContext.None) return;

            if (handler.context.HasFlag(SyncContext.CurrentMap))
                data.MpContext().map = Find.CurrentMap;

            if (handler.context.HasFlag(SyncContext.MapMouseCell))
            {
                data.MpContext().map = Find.CurrentMap;
                SyncSerialization.WriteSync(data, UI.MouseCell());
            }

            if (handler.context.HasFlag(SyncContext.MapSelected))
                SyncSerialization.WriteSync(data, Find.Selector.selected.Cast<ISelectable>().ToList());

            if (handler.context.HasFlag(SyncContext.WorldSelected))
                SyncSerialization.WriteSync(data, Find.WorldSelector.selected.Cast<ISelectable>().ToList());

            if (handler.context.HasFlag(SyncContext.QueueOrder_Down))
                data.WriteBool(KeyBindingDefOf.QueueOrder.IsDownEvent);
        }
    }
}
