extern alias zip;
using System;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public static class ConstantTicker
    {
        public static bool ticking;

        public static void Tick()
        {
            ticking = true;

            try
            {
                //TickResearch();

                // Not really deterministic but here for possible future server-side game state verification
                //Extensions.PushFaction(null, Multiplayer.RealPlayerFaction);
                //TickSync();
                //SyncResearch.ConstantTick();
                //Extensions.PopFaction();

                TickShipCountdown();
                TickSyncCoordinator();
                TickAutosave();
            }
            finally
            {
                ticking = false;
            }
        }

        static void TickAutosave()
        {
            if (Multiplayer.LocalServer is not { } server) return;

            if (server.settings.autosaveUnit == AutosaveUnit.Minutes)
            {
                var session = Multiplayer.session;
                session.autosaveCounter++;

                if (server.settings.autosaveInterval > 0 &&
                    session.autosaveCounter > server.settings.autosaveInterval * 60 * 60)
                {
                    session.autosaveCounter = 0;
                    MultiplayerSession.DoAutosave();
                }
            } else if (server.settings.autosaveUnit == AutosaveUnit.Days && server.settings.autosaveInterval > 0)
            {
                var anyMapCounterUp =
                    Multiplayer.game.mapComps
                    .Any(m => m.autosaveCounter > server.settings.autosaveInterval * 2500 * 24);

                if (anyMapCounterUp)
                {
                    Multiplayer.game.mapComps.Do(m => m.autosaveCounter = 0);
                    MultiplayerSession.DoAutosave();
                }
            }
        }

        static void TickSyncCoordinator()
        {
            var sync = Multiplayer.game.sync;
            if (sync.ShouldCollect && TickPatch.Timer % 30 == 0 && sync.currentOpinion != null)
            {
                if (!TickPatch.Simulating && (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance))
                    Multiplayer.Client.SendFragmented(Packets.Client_SyncInfo, sync.currentOpinion.Serialize());

                sync.AddClientOpinionAndCheckDesync(sync.currentOpinion);
                sync.currentOpinion = null;
            }
        }

        // Moved from RimWorld.ShipCountdown because the original one is called from Update
        private static void TickShipCountdown()
        {
            if (ShipCountdown.timeLeft > 0f)
            {
                ShipCountdown.timeLeft -= 1 / 60f;

                if (ShipCountdown.timeLeft <= 0f)
                    ShipCountdown.CountdownEnded();
            }
        }

        private static Func<BufferTarget, BufferData, bool> bufferPredicate = SyncFieldUtil.BufferedChangesPruner(() => TickPatch.Timer);

        private static void TickSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                if (!f.inGameLoop) continue;
                SyncFieldUtil.bufferedChanges[f].RemoveAll(bufferPredicate);
            }
        }

        private static Pawn dummyPawn = new Pawn()
        {
            relations = new Pawn_RelationsTracker(dummyPawn),
        };

        public static void TickResearch()
        {
            MultiplayerWorldComp comp = Multiplayer.WorldComp;
            foreach (FactionWorldData factionData in comp.factionData.Values)
            {
                if (factionData.researchManager.currentProj == null)
                    continue;

                Extensions.PushFaction(null, factionData.factionId);

                foreach (var kv in factionData.researchSpeed.data)
                {
                    Pawn pawn = PawnsFinder.AllMaps_Spawned.FirstOrDefault(p => p.thingIDNumber == kv.Key);
                    if (pawn == null)
                    {
                        dummyPawn.factionInt = Faction.OfPlayer;
                        pawn = dummyPawn;
                    }

                    Find.ResearchManager.ResearchPerformed(kv.Value, pawn);

                    dummyPawn.factionInt = null;
                }

                Extensions.PopFaction();
            }
        }
    }

    [HarmonyPatch(typeof(ShipCountdown), nameof(ShipCountdown.CancelCountdown))]
    static class CancelCancelCountdown
    {
        static bool Prefix() => Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing;
    }

    [HarmonyPatch(typeof(ShipCountdown), nameof(ShipCountdown.ShipCountdownUpdate))]
    static class ShipCountdownUpdatePatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

}
