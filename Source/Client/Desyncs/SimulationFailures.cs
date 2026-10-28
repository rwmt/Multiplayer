using System;
using Multiplayer.Client.Util;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Desyncs
{
    // A command/tick exception is caught and skipped so the simulation keeps
    // running, but the skip means this client may no longer match the others -
    // record it and warn the player once instead of only burying it in the log
    public static class SimulationFailures
    {
        public static void Handle(string context, Exception e)
        {
            MpLog.Error($"{context}: {e}");

            var session = Multiplayer.session;
            if (session == null)
                return;

            session.simulationFailures++;
            if (session.simulationFailures > 1)
                return;

            session.firstSimulationFailure = $"[{TickPatch.Timer}] {context}: {e.GetType().Name}: {e.Message}";

            // historical: false - archiving mutates game state, and the failure
            // may have happened on this client only
            Messages.Message(
                "MP: a synced command or tick failed on this client and was skipped. " +
                "This can lead to a desync - check the log for the error.",
                MessageTypeDefOf.NegativeEvent, historical: false);
        }
    }
}
