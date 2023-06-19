using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public static bool DesignateSingleCell(Designator __instance, IntVec3 __0)
        {
            if (!Multiplayer.InInterface) return true;

            Designator designator = __instance;

            Map map = Find.CurrentMap;
            LoggingByteWriter writer = new LoggingByteWriter();
            writer.Log.Node("Designate single cell: " + designator.GetType());

            WriteData(writer, DesignatorMode.SingleCell, designator);
            SyncSerialization.WriteSync(writer, __0);

            SendSyncCommand(map.uniqueID, writer);
            Multiplayer.WriterLog.AddCurrentNode(writer);

            return false;
        }

        public static bool DesignateMultiCell(Designator __instance, IEnumerable<IntVec3> __0)
        {
            if (!Multiplayer.InInterface) return true;

            // No cells implies Finalize(false), which currently doesn't cause side effects
            if (!__0.Any()) return true;

            Designator designator = __instance;

            Map map = Find.CurrentMap;
            LoggingByteWriter writer = new LoggingByteWriter();
            writer.Log.Node("Designate multi cell: " + designator.GetType());
            IntVec3[] cellArray = __0.ToArray();

            WriteData(writer, DesignatorMode.MultiCell, designator);
            SyncSerialization.WriteSync(writer, cellArray);

            SendSyncCommand(map.uniqueID, writer);
            Multiplayer.WriterLog.AddCurrentNode(writer);

            return false;
        }

        public static bool DesignateThing(Designator __instance, Thing __0)
        {
            if (!Multiplayer.InInterface) return true;

            Designator designator = __instance;

            Map map = Find.CurrentMap;
            LoggingByteWriter writer = new LoggingByteWriter();
            writer.Log.Node("Designate thing: " + __0 + " " + designator.GetType());

            WriteData(writer, DesignatorMode.Thing, designator);
            SyncSerialization.WriteSync(writer, __0);

            SendSyncCommand(map.uniqueID, writer);
            Multiplayer.WriterLog.AddCurrentNode(writer);

            FleckMaker.ThrowMetaPuffs(__0);

            return false;
        }

        private static void SendSyncCommand(int mapId, ByteWriter data)
        {
            if (!Multiplayer.GhostMode)
                Multiplayer.Client.SendCommand(CommandType.Designator, mapId, data.ToArray());
        }

        // DesignateFinalizer ignores unimplemented Designate* methods
        public static Exception DesignateFinalizer(Exception __exception)
        {
            if (__exception is NotImplementedException) {
                return null;
            }

            return __exception;
        }

        private static void WriteData(ByteWriter data, DesignatorMode mode, Designator designator)
        {
            SyncSerialization.WriteSync(data, mode);
            SyncSerialization.WriteSyncObject(data, designator, designator.GetType());

            // Read at MapAsyncTimeComp.SetDesignatorState
            // The reading side affects global state so these can't be SyncWorkers

            if (designator is Designator_AreaAllowed)
                SyncSerialization.WriteSync(data, Designator_AreaAllowed.SelectedArea);

            if (designator is Designator_Install install)
                SyncSerialization.WriteSync(data, install.MiniToInstallOrBuildingToReinstall);

            if (designator is Designator_Zone)
                SyncSerialization.WriteSync(data, Find.Selector.SelectedZone);
        }
    }

    [HarmonyPatch(typeof(Designator_Install))]
    [HarmonyPatch(nameof(Designator_Install.MiniToInstallOrBuildingToReinstall), MethodType.Getter)]
    public static class DesignatorInstall_SetThingToInstall
    {
        public static Thing thingToInstall;

        static void Postfix(ref Thing __result)
        {
            if (thingToInstall != null)
                __result = thingToInstall;
        }
    }

    [HarmonyPatch(typeof(Designator_Cancel))]
    [HarmonyPatch(nameof(Designator_Cancel.DesignateThing))]
    public static class DesignatorCancelPatch
    {
        static void Postfix(Thing t)
        {
            if (Multiplayer.Client != null && (t is Frame || t is Blueprint))
                Find.Selector.Deselect(t);
        }
    }

    [HarmonyPatch(typeof(Designator_Install))]
    [HarmonyPatch(nameof(Designator_Install.DesignateSingleCell))]
    public static class DesignatorInstall_CancelBlueprints
    {
        // Returns bool to make it a cancellable prefix
        static bool Prefix(Designator_Install __instance)
        {
            // This gets called in ProcessInput which is enough in vanilla but not with multiple players
            Thing miniToInstallOrBuildingToReinstall = __instance.MiniToInstallOrBuildingToReinstall;
            if (miniToInstallOrBuildingToReinstall != null)
                InstallBlueprintUtility.CancelBlueprintsFor(miniToInstallOrBuildingToReinstall);
            return true;
        }
    }
}
