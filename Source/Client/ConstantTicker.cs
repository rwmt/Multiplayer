extern alias zip;

using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using System.Linq;
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

                var sync = Multiplayer.game.sync;
                if (sync.ShouldCollect && TickPatch.Timer % 30 == 0 && sync.currentOpinion != null)
                {
                    if (!TickPatch.Skipping && (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance))
                        Multiplayer.Client.SendFragmented(Packets.Client_SyncInfo, sync.currentOpinion.Serialize());

                    sync.AddClientOpinionAndCheckDesync(sync.currentOpinion);
                    sync.currentOpinion = null;
                }
            }
            finally
            {
                ticking = false;
            }
        }

        // Moved from ShipCountdown because the original one is called from Update
        private static void TickShipCountdown()
        {
            if (ShipCountdown.timeLeft > 0f)
            {
                ShipCountdown.timeLeft -= 1 / 60f;

                if (ShipCountdown.timeLeft <= 0f)
                    ShipCountdown.CountdownEnded();
            }
        }

        private static void TickSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                if (!f.inGameLoop) continue;

                Sync.bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (OnMainThread.CheckShouldRemove(f, k, data))
                        return true;

                    if (!data.sent && TickPatch.Timer - data.timestamp > 30)
                    {
                        f.DoSync(k.Item1, data.toSend, k.Item2);
                        data.sent = true;
                        data.timestamp = TickPatch.Timer;
                    }

                    return false;
                });
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
