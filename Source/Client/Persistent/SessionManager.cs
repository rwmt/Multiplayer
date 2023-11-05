using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent;

public class SessionManager : IHasSemiPersistentData
{
    public IReadOnlyList<ISession> AllSessions => allSessions.AsReadOnly();
    public IReadOnlyList<IExposableSession> ExposableSessions => exposableSessions.AsReadOnly();
    public IReadOnlyList<ISemiPersistentSession> SemiPersistentSessions => semiPersistentSessions.AsReadOnly();
    public IReadOnlyList<ITickingSession> TickingSessions => tickingSessions.AsReadOnly();

    private List<ISession> allSessions = new();
    private List<IExposableSession> exposableSessions = new();
    private List<ISemiPersistentSession> semiPersistentSessions = new();
    private List<ITickingSession> tickingSessions = new();
    private static HashSet<Type> tempCleanupLoggingTypes = new();

    public bool AnySessionActive => allSessions.Count > 0;

    /// <summary>
    /// Adds a new session to the list of active sessions.
    /// </summary>
    /// <param name="session">The session to try to add to active sessions.</param>
    /// <returns><see langword="true"/> if the session was added to active ones, <see langword="false"/> if there was a conflict between sessions.</returns>
    public bool AddSession(ISession session)
    {
        if (GetFirstConflictingSession(session) != null)
            return false;

        AddSessionNoCheck(session);
        return true;
    }

    /// <summary>
    /// Tries to get a conflicting session (through the use of <see cref="ISessionWithCreationRestrictions"/>) or, if there was none, returns the input <paramref name="session"/>.
    /// </summary>
    /// <param name="session">The session to try to add to active sessions.</param>
    /// <returns>A session that was conflicting with the input one, or the input itself if there were no conflicts. It may be of a different type than the input.</returns>
    public ISession GetOrAddSessionAnyConflict(ISession session)
    {
        if (GetFirstConflictingSession(session) is { } other)
            return other;

        AddSessionNoCheck(session);
        return session;
    }

    /// <summary>
    /// Tries to get a conflicting session (through the use of <see cref="ISessionWithCreationRestrictions"/>) or, if there was none, returns the input <paramref name="session"/>.
    /// </summary>
    /// <param name="session">The session to try to add to active sessions.</param>
    /// <returns>A session that was conflicting with the input one if it's the same type (<c>other is T</c>), null if it's a different type, or the input itself if there were no conflicts.</returns>
    public T GetOrAddSession<T>(T session) where T : ISession
    {
        if (session is ISessionWithCreationRestrictions s)
        {
            var conflicting = false;

            // Look for the first conflicting session of the same type as the input
            foreach (var other in allSessions)
            {
                if (s.CanExistWith(other))
                    continue;
                // If we found a conflicting session of same type, return it
                if (other is T o)
                    return o;
                conflicting = true;
            }

            // If there was a conflict but not of the same type as input, return null
            if (conflicting)
                return default;
        }

        AddSessionNoCheck(session);
        return session;
    }

    private void AddSessionNoCheck(ISession session)
    {
        if (session is IExposableSession exposable)
            exposableSessions.Add(exposable);
        else if (session is ISemiPersistentSession semiPersistent)
            semiPersistentSessions.Add(semiPersistent);

        if (session is ITickingSession ticking)
            tickingSessions.Add(ticking);

        allSessions.Add(session);
        session.SessionId = UniqueIDsManager.GetNextID(ref Multiplayer.GameComp.nextSessionId);
        session.PostAddSession();
    }

    /// <summary>
    /// Tries to remove a session from active ones.
    /// </summary>
    /// <param name="session">The session to try to remove from the active sessions.</param>
    /// <returns><see langword="true"/> if successfully removed from <see cref="AllSessions"/>. Doesn't correspond to if it was successfully removed from other lists of sessions.</returns>
    public bool RemoveSession(ISession session)
    {
        if (session is IExposableSession exposable)
            exposableSessions.Remove(exposable);
        else if (session is ISemiPersistentSession semiPersistent)
            semiPersistentSessions.Remove(semiPersistent);

        if (session is ITickingSession ticking)
            tickingSessions.Remove(ticking);

        if (allSessions.Remove(session))
        {
            // Avoid repeated calls if the session was already removed
            session.PostRemoveSession();
            return true;
        }

        return false;
    }

    private ISession GetFirstConflictingSession(ISession session)
    {
        // Should the check be two-way? A property for optional two-way check?
        if (session is ISessionWithCreationRestrictions restrictions)
            return allSessions.FirstOrDefault(s => !restrictions.CanExistWith(s));

        return null;
    }

    /// <summary>
    /// Ticks over <see cref="TickingSessions"/>. It iterates backwards using a <see langword="for"/> loop to make it safe for the sessions to remove themselves when ticking.
    /// </summary>
    public void TickSessions()
    {
        for (int i = tickingSessions.Count - 1; i >= 0; i--)
            tickingSessions[i].Tick();
    }

    public T GetFirstOfType<T>() where T : ISession => allSessions.OfType<T>().FirstOrDefault();

    public T GetFirstWithId<T>(int id) where T : ISession => allSessions.OfType<T>().FirstOrDefault(s => s.SessionId == id);

    public ISession GetFirstWithId(int id) => allSessions.FirstOrDefault(s => s.SessionId == id);

    public void WriteSemiPersistent(ByteWriter data)
    {
        // Clear the set to make sure it's empty
        tempCleanupLoggingTypes.Clear();
        for (int i = semiPersistentSessions.Count - 1; i >= 0; i--)
        {
            var session = semiPersistentSessions[i];
            if (!session.IsSessionValid)
            {
                semiPersistentSessions.RemoveAt(i);
                allSessions.Remove(session);
                var sessionType = session.GetType();
                if (!tempCleanupLoggingTypes.Add(sessionType))
                    Log.Message($"Multiplayer session not valid after exposing data: {sessionType}");
            }
        }
        // Clear the set again to not leave behind any unneeded stuff
        tempCleanupLoggingTypes.Clear();

        data.WriteInt32(semiPersistentSessions.Count);

        foreach (var session in semiPersistentSessions)
        {
            data.WriteUShort((ushort)ImplSerialization.sessions.FindIndex(session.GetType()));
            data.WriteInt32(session.Map?.uniqueID ?? -1);

            try
            {
                session.Write(new WritingSyncWorker(data));
            }
            catch (Exception e)
            {
                Log.Error($"Trying to write semi persistent session for {session} failed with exception:\n{e}");
            }
        }
    }

    public void ReadSemiPersistent(ByteReader data)
    {
        var sessionsCount = data.ReadInt32();
        semiPersistentSessions.Clear();
        allSessions.RemoveAll(s => s is ISemiPersistentSession);

        for (int i = 0; i < sessionsCount; i++)
        {
            ushort typeIndex = data.ReadUShort();
            int mapId = data.ReadInt32();

            if (typeIndex >= ImplSerialization.sessions.Length)
            {
                Log.Error($"Received data for ISession type with index out of range: {typeIndex}, session types count: {ImplSerialization.sessions.Length}");
                continue;
            }

            var objType = ImplSerialization.sessions[typeIndex];
            Map map = null;
            if (mapId != -1)
            {
                map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                if (map == null)
                {
                    Log.Error($"Trying to read semi persistent session of type {objType} received a null map while expecting a map with ID {mapId}");
                    // Continue? Let it run?
                }
            }

            try
            {
                if (Activator.CreateInstance(objType, map) is ISemiPersistentSession session)
                {
                    session.Read(new ReadingSyncWorker(data));
                    semiPersistentSessions.Add(session);
                    allSessions.Add(session);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Trying to read semi persistent session of type {objType} failed with exception:\n{e}");
            }
        }
    }

    public void ExposeSessions()
    {
        Scribe_Collections.Look(ref exposableSessions, "sessions", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            allSessions ??= new();
            exposableSessions ??= new();
            semiPersistentSessions ??= new();

            // Clear the set to make sure it's empty
            tempCleanupLoggingTypes.Clear();
            for (int i = exposableSessions.Count - 1; i >= 0; i--)
            {
                var session = exposableSessions[i];
                if (!session.IsSessionValid)
                {
                    // Removal from allSessions handled lower
                    exposableSessions.RemoveAt(i);
                    session.PostRemoveSession();
                    var sessionType = session.GetType();
                    if (!tempCleanupLoggingTypes.Add(sessionType))
                        Log.Message($"Multiplayer session not valid after exposing data: {sessionType}");
                }
            }
            // Clear the set again to not leave behind any unneeded stuff
            tempCleanupLoggingTypes.Clear();

            // Just in case something went wrong when exposing data, clear the all session from exposable ones and fill them again
            allSessions.RemoveAll(s => s is IExposableSession);
            allSessions.AddRange(exposableSessions);
        }
    }

    public bool IsAnySessionCurrentlyPausing(Map map)
    {
        for (int i = 0; i < allSessions.Count; i++)
        {
            if (AllSessions[i].IsCurrentlyPausing(map))
                return true;
        }

        return false;
    }

    public static void ValidateSessionClasses()
    {
        foreach (var subclass in typeof(ISession).AllSubclasses())
        {
            var interfaces = subclass.GetInterfaces();

            if (interfaces.Contains(typeof(ISemiPersistentSession)))
            {
                if (interfaces.Contains(typeof(IExposableSession)))
                    Log.Error($"Type {subclass} implements both {nameof(IExposableSession)} and {nameof(ISemiPersistentSession)}, it should implement only one of them at most.");

                if (AccessTools.GetDeclaredConstructors(subclass).All(c => c.GetParameters().Length != 1 || c.GetParameters()[0].ParameterType != typeof(Map)))
                    Log.Error($"Type {subclass} implements {nameof(ISemiPersistentSession)}, but does not have a single parameter constructor with {nameof(Map)} as the parameter.");
            }
        }
    }
}
