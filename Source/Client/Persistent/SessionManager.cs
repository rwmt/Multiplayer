using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent;

public class SessionManager : IHasSessionData, ISessionManager
{
    public IReadOnlyList<Session> AllSessions => allSessions.AsReadOnly();
    public IReadOnlyList<ExposableSession> ExposableSessions => exposableSessions.AsReadOnly();
    public IReadOnlyList<SemiPersistentSession> SemiPersistentSessions => semiPersistentSessions.AsReadOnly();
    public IReadOnlyList<ITickingSession> TickingSessions => tickingSessions.AsReadOnly();

    private List<Session> allSessions = new();
    private List<ExposableSession> exposableSessions = new();
    private List<SemiPersistentSession> semiPersistentSessions = new();
    private List<ITickingSession> tickingSessions = new();
    private static HashSet<Type> tempCleanupLoggingTypes = new();

    public Map Map { get; }
    public bool AnySessionActive => allSessions.Count > 0;

    public SessionManager(Map map)
    {
        Map = map;
    }

    public bool AddSession(Session session)
    {
        if (GetFirstConflictingSession(session) != null)
            return false;

        AddSessionNoCheck(session);
        return true;
    }

    public Session GetOrAddSessionAnyConflict(Session session)
    {
        if (GetFirstConflictingSession(session) is { } other)
            return other;

        AddSessionNoCheck(session);
        return session;
    }

    public T GetOrAddSession<T>(T session) where T : Session
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

    private void AddSessionNoCheck(Session session)
    {
        if (session is ExposableSession exposable)
            exposableSessions.Add(exposable);
        else if (session is SemiPersistentSession semiPersistent)
            semiPersistentSessions.Add(semiPersistent);

        if (session is ITickingSession ticking)
            tickingSessions.Add(ticking);

        allSessions.Add(session);
        session.SessionId = UniqueIDsManager.GetNextID(ref Multiplayer.GameComp.nextSessionId);
        session.PostAddSession();
    }

    public bool RemoveSession(Session session)
    {
        if (session is ExposableSession exposable)
            exposableSessions.Remove(exposable);
        else if (session is SemiPersistentSession semiPersistent)
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

    private Session GetFirstConflictingSession(Session session)
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

    public T GetFirstOfType<T>() where T : Session => allSessions.OfType<T>().FirstOrDefault();

    public T GetFirstWithId<T>(int id) where T : Session => allSessions.OfType<T>().FirstOrDefault(s => s.SessionId == id);

    public Session GetFirstWithId(int id) => allSessions.FirstOrDefault(s => s.SessionId == id);

    public void WriteSessionData(ByteWriter data)
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
                    Log.Message($"Multiplayer session not valid after writing semi persistent data: {sessionType}");
            }
        }
        // Clear the set again to not leave behind any unneeded stuff
        tempCleanupLoggingTypes.Clear();

        data.WriteInt32(semiPersistentSessions.Count);

        foreach (var session in semiPersistentSessions)
        {
            data.WriteUShort((ushort)ImplSerialization.sessions.FindIndex(session.GetType()));
            data.WriteInt32(session.SessionId);

            try
            {
                session.Sync(new WritingSyncWorker(data));
            }
            catch (Exception e)
            {
                Log.Error($"Trying to write semi persistent session for {session} failed with exception:\n{e}");
            }
        }
    }

    public void ReadSessionData(ByteReader data)
    {
        var sessionsCount = data.ReadInt32();
        semiPersistentSessions.Clear();
        allSessions.RemoveAll(s => s is SemiPersistentSession);

        for (int i = 0; i < sessionsCount; i++)
        {
            ushort typeIndex = data.ReadUShort();
            int sessionId = data.ReadInt32();

            if (typeIndex >= ImplSerialization.sessions.Length)
            {
                Log.Error($"Received data for ISession type with index out of range: {typeIndex}, session types count: {ImplSerialization.sessions.Length}");
                continue;
            }

            var objType = ImplSerialization.sessions[typeIndex];

            try
            {
                if (Activator.CreateInstance(objType, Map) is SemiPersistentSession session)
                {
                    session.SessionId = sessionId;
                    session.Sync(new ReadingSyncWorker(data));
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
        Scribe_Collections.Look(ref exposableSessions, "sessions", LookMode.Deep, Map);

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
            allSessions.RemoveAll(s => s is ExposableSession);
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
}
