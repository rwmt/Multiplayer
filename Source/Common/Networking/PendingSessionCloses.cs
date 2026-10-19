using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Multiplayer.Common;

/// <summary>
/// Transport sessions waiting to be closed, by remote. Closing a session can discard packets the transport hasn't
/// sent yet, so a close that follows a goodbye waits a little. Thread safe: the server thread schedules and takes
/// closes while it runs, another thread takes the rest once it stopped.
/// </summary>
public class PendingSessionCloses<TRemote> where TRemote : notnull
{
    private readonly object locker = new();
    private readonly Dictionary<TRemote, long> dueAt = new();
    private readonly Func<long> clockMs;

    public PendingSessionCloses(Func<long>? clockMs = null)
    {
        if (clockMs == null)
        {
            var watch = Stopwatch.StartNew();
            clockMs = () => watch.ElapsedMilliseconds;
        }

        this.clockMs = clockMs;
    }

    public int Count
    {
        get { lock (locker) return dueAt.Count; }
    }

    /// Replaces an earlier schedule for the remote: the later goodbye needs the time too.
    public void Schedule(TRemote remote, int delayMs)
    {
        lock (locker)
            dueAt[remote] = clockMs() + delayMs;
    }

    /// Returns whether a close was pending.
    public bool Cancel(TRemote remote)
    {
        lock (locker)
            return dueAt.Remove(remote);
    }

    /// Removes and returns the remotes whose delay is over.
    public IReadOnlyList<TRemote> TakeDue()
    {
        lock (locker)
        {
            if (dueAt.Count == 0) return Array.Empty<TRemote>();

            var now = clockMs();
            List<TRemote>? due = null;
            foreach (var entry in dueAt)
                if (now >= entry.Value)
                    (due ??= []).Add(entry.Key);

            if (due == null) return Array.Empty<TRemote>();

            foreach (var remote in due)
                dueAt.Remove(remote);
            return due;
        }
    }
}
