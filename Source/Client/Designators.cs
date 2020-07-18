﻿using HarmonyLib;
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
        public static bool DesignateSingleCell(Designator __instance, IntVec3 __0)
        {
            if (!Multiplayer.ShouldSync) return true;

            Designator designator = __instance;

            Map map = Find.CurrentMap;
            LoggingByteWriter writer = new LoggingByteWriter();
            writer.LogNode("Designate single cell: " + designator.GetType());

            WriteData(writer, DesignatorMode.SingleCell, designator);
            Sync.WriteSync(writer, __0);

            Multiplayer.Client.SendCommand(CommandType.Designator, map.uniqueID, writer.ToArray());
            Multiplayer.PacketLog.nodes.Add(writer.current);

            return false;
        }

        public static bool DesignateMultiCell(Designator __instance, IEnumerable<IntVec3> __0)
        {
            if (!Multiplayer.ShouldSync) return true;

            // No cells implies Finalize(false), which currently doesn't cause side effects
            if (__0.Count() == 0) return true;

            Designator designator = __instance;

            Map map = Find.CurrentMap;
            LoggingByteWriter writer = new LoggingByteWriter();
            writer.LogNode("Designate multi cell: " + designator.GetType());
            IntVec3[] cellArray = __0.ToArray();

            WriteData(writer, DesignatorMode.MultiCell, designator);
            Sync.WriteSync(writer, cellArray);

            Multiplayer.Client.SendCommand(CommandType.Designator, map.uniqueID, writer.ToArray());
            Multiplayer.PacketLog.nodes.Add(writer.current);

            return false;
        }

        public static bool DesignateThing(Designator __instance, Thing __0)
        {
            if (!Multiplayer.ShouldSync) return true;

            Designator designator = __instance;

            Map map = Find.CurrentMap;
            LoggingByteWriter writer = new LoggingByteWriter();
            writer.LogNode("Designate thing: " + __0 + " " + designator.GetType());

            WriteData(writer, DesignatorMode.Thing, designator);
            Sync.WriteSync(writer, __0);

            Multiplayer.Client.SendCommand(CommandType.Designator, map.uniqueID, writer.ToArray());
            Multiplayer.PacketLog.nodes.Add(writer.current);

            MoteMaker.ThrowMetaPuffs(__0);

            return false;
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
            Sync.WriteSync(data, mode);
            Sync.WriteSyncObject(data, designator, designator.GetType());

            // These affect the Global context and shouldn't be SyncWorkers
            // Read at MapAsyncTimeComp.SetDesignatorState

            if (designator is Designator_AreaAllowed)
                Sync.WriteSync(data, Designator_AreaAllowed.SelectedArea);

            if (designator is Designator_Install install)
                Sync.WriteSync(data, install.MiniToInstallOrBuildingToReinstall);

            if (designator is Designator_Zone)
                Sync.WriteSync(data, Find.Selector.SelectedZone);
        }
    }

    [HarmonyPatch(typeof(Designator_Install))]
    [HarmonyPatch(nameof(Designator_Install.MiniToInstallOrBuildingToReinstall), MethodType.Getter)]
    public static class DesignatorInstallPatch
    {
        public static Thing thingToInstall;

        static void Postfix(ref Thing __result)
        {
            if (thingToInstall != null)
                __result = thingToInstall;
        }
    }

}
