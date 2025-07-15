using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using System;
using Verse;

namespace Multiplayer.Client.Persistent
{
    [HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.SetupCamp))]
    internal static class SetupCampPatch
    {
        static void Postfix(Command __result, Caravan caravan)
        {
            if (Multiplayer.Client == null || __result is not Command_Action cmd)
                return;

            cmd.action = () => Sync_Camp(caravan);
        }

        [SyncMethod]
        private static void Sync_Camp(Caravan caravan)
        {
            if (caravan == null || caravan.Faction == null)
            {
                MpLog.Error("[SettleInEmptyTileUtility.SetupCamp] Null caravan or faction in Sync_Camp");
                return;
            }

            // Create the camp using the vanilla logic exactly as in SetupCamp
            LongEventHandler.QueueLongEvent(delegate
            {
                TileFactionContext.SetFactionForTile(caravan.Tile, caravan.Faction);

                Map map = GetOrGenerateMapUtility.GetOrGenerateMap(caravan.Tile, Find.World.info.initialMapSize, WorldObjectDefOf.Camp);
                
                // Set the faction on the camp world object (this is what vanilla does)
                map.Parent.SetFaction(caravan.Faction);

                // Enter the caravan into the map
                Pawn pawn = caravan.PawnsListForReading[0];
                CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Center, CaravanDropInventoryMode.DoNotDrop, draftColonists: false, (IntVec3 x) => x.GetRoom(map).CellCount >= 600);
                map.Parent.GetComponent<TimedDetectionRaids>()?.StartDetectionCountdown(240000, 60000);
                CameraJumper.TryJump(pawn);

            }, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
        }
    }
}
