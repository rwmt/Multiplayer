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
                TickShipCountdown();
                TickSyncCoordinator();
                TickAutosave();
            }
            finally
            {
                ticking = false;
            }
        }

        private const float TicksPerMinute = 60 * 60;
        private const float TicksPerIngameDay = 2500 * 24;

        private static void TickAutosave()
        {
            if (Multiplayer.LocalServer is not { } server) return;

            if (server.settings.autosaveUnit == AutosaveUnit.Minutes)
            {
                var session = Multiplayer.session;
                session.autosaveCounter++;

                if (server.settings.autosaveInterval > 0 &&
                    session.autosaveCounter > server.settings.autosaveInterval * TicksPerMinute)
                {
                    session.autosaveCounter = 0;
                    Autosaving.DoAutosave();
                }
            } else if (server.settings.autosaveUnit == AutosaveUnit.Days && server.settings.autosaveInterval > 0)
            {
                var anyMapCounterUp =
                    Multiplayer.game.mapComps
                    .Any(m => m.autosaveCounter > server.settings.autosaveInterval * TicksPerIngameDay);

                if (anyMapCounterUp)
                {
                    Multiplayer.game.mapComps.Do(m => m.autosaveCounter = 0);
                    Autosaving.DoAutosave();
                }
            }
        }

        private static void TickSyncCoordinator()
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
