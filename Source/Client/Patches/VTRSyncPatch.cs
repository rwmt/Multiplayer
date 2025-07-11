using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(GenTicks), nameof(GenTicks.GetCameraUpdateRate))]
    public static class VTRSyncPatch
    {
        static bool Prefix(Thing thing, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = GetSynchronizedUpdateRate(thing);
            return false;
        }

        private static int GetSynchronizedUpdateRate(Thing thing) => thing?.MapHeld?.AsyncTime()?.VTR ?? 15;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.CurrentMap), MethodType.Setter)]
    static class MapSwitchPatch
    {
        private static int lastSentFromMap = int.MaxValue;
        private static int lastSentTick = -1;

        static void Prefix(Map value)
        {
            if (Multiplayer.Client == null) return;

            try
            {
                int previousMap = Find.CurrentMap?.uniqueID ?? -1;
                int newMap = value?.uniqueID ?? -1;
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                // Only send when multiplayer is ready
                if (Multiplayer.Client == null || Client.Multiplayer.session == null)
                    return;

                // If no change in map, do nothing
                if (previousMap == newMap)
                    return;

                // Prevent duplicate commands for the same transition, but allow retry after a tick
                if (lastSentFromMap == previousMap && currentTick == lastSentTick)
                    return;

                // Send map change command to server
                // Send as global command since it affects multiple maps
                Multiplayer.Client.SendCommand(CommandType.PlayerCount, ScheduledCommand.Global, ByteWriter.GetBytes(previousMap, newMap));

                // Track this command to prevent duplicates
                lastSentFromMap = previousMap;
                lastSentTick = currentTick;
            }
            catch (Exception ex)
            {
                MpLog.Error($"VTR MapSwitchPatch error: {ex.Message}");
            }
        }
    }
}
