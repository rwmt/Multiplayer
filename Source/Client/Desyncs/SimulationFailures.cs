using System;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

/// <summary>
/// Exceptions in synced commands and ticks which are caught so that the rest of the simulation goes on. Each one
/// may have left this client different from the others, so they are counted, the player is told once per session
/// and desync reports include them.
/// </summary>
public static class SimulationFailures
{
    /// The world from the server replaces whatever the failures did (joining, rejoining after a desync)
    public static void Reset(MultiplayerSession session)
    {
        session.simulationFailures = 0;
        session.firstSimulationFailure = null;
    }

    /// Call from the catch block, next to logging the exception. Touches only local state.
    public static void Record(string where, Exception e)
    {
        var session = Multiplayer.session;
        if (session == null) return;

        session.simulationFailures++;
        if (session.firstSimulationFailure != null) return;

        session.firstSimulationFailure =
            $"tick {TickPatch.Timer}, {where}: {e.GetType().Name}: {e.Message}".Replace('\n', ' ').Replace('\r', ' ');

        // Not from inside the tick: the message is local UI and the failure may have happened on this client only.
        // Not translated: the strings live in the separate locale repository.
        OnMainThread.Enqueue(() =>
        {
            if (Multiplayer.session != session || session.firstSimulationFailure == null) return;

            const string text = "Multiplayer: an error in the game simulation was skipped on this client. " +
                                "This can cause a desync, the log has the details.";
            Messages.Message(text, MessageTypeDefOf.NegativeEvent, historical: false);
            session.AddMsg($"{text} ({session.firstSimulationFailure})", rawMessage: true);
        });
    }
}
