using System.Collections.Generic;
using Multiplayer.Client.Persistent;
using Verse;

namespace Multiplayer.Client.Experimental;

public interface ISessionManager
{
    IReadOnlyList<ISession> AllSessions { get; }
    IReadOnlyList<IExposableSession> ExposableSessions { get; }
    IReadOnlyList<ISemiPersistentSession> SemiPersistentSessions { get; }
    IReadOnlyList<ITickingSession> TickingSessions { get; }
    bool AnySessionActive { get; }

    /// <summary>
    /// Adds a new session to the list of active sessions.
    /// </summary>
    /// <param name="session">The session to try to add to active sessions.</param>
    /// <returns><see langword="true"/> if the session was added to active ones, <see langword="false"/> if there was a conflict between sessions.</returns>
    bool AddSession(ISession session);

    /// <summary>
    /// Tries to get a conflicting session (through the use of <see cref="ISessionWithCreationRestrictions"/>) or, if there was none, returns the input <paramref name="session"/>.
    /// </summary>
    /// <param name="session">The session to try to add to active sessions.</param>
    /// <returns>A session that was conflicting with the input one, or the input itself if there were no conflicts. It may be of a different type than the input.</returns>
    ISession GetOrAddSessionAnyConflict(ISession session);

    /// <summary>
    /// Tries to get a conflicting session (through the use of <see cref="ISessionWithCreationRestrictions"/>) or, if there was none, returns the input <paramref name="session"/>.
    /// </summary>
    /// <param name="session">The session to try to add to active sessions.</param>
    /// <returns>A session that was conflicting with the input one if it's the same type (<c>other is T</c>), null if it's a different type, or the input itself if there were no conflicts.</returns>
    T GetOrAddSession<T>(T session) where T : ISession;

    /// <summary>
    /// Tries to remove a session from active ones.
    /// </summary>
    /// <param name="session">The session to try to remove from the active sessions.</param>
    /// <returns><see langword="true"/> if successfully removed from <see cref="AllSessions"/>. Doesn't correspond to if it was successfully removed from other lists of sessions.</returns>
    bool RemoveSession(ISession session);

    T GetFirstOfType<T>() where T : ISession;

    T GetFirstWithId<T>(int id) where T : ISession;

    ISession GetFirstWithId(int id);

    bool IsAnySessionCurrentlyPausing(Map map); // Is it necessary for the API?
}
