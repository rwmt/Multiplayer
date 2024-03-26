using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Multiplayer.API;
using Multiplayer.Common;

namespace Multiplayer.Client;

public class SyncWorkerDictionary : IEnumerable<SyncWorkerEntry>
{
    protected readonly Dictionary<Type, SyncWorkerEntry> explicitEntries = new();

    public SyncWorkerEntry GetOrAddEntry(Type type, bool shouldConstruct = false)
    {
        if (explicitEntries.TryGetValue(type, out SyncWorkerEntry explicitEntry))
            return explicitEntry;

        return AddExplicit(type, shouldConstruct);
    }

    protected SyncWorkerEntry AddExplicit(Type type, bool shouldConstruct = false)
    {
        var explicitEntry = new SyncWorkerEntry(type, shouldConstruct);
        explicitEntries.Add(type, explicitEntry);
        return explicitEntry;
    }

    public void Add<T>(SyncWorkerDelegate<T> action)
    {
        var entry = GetOrAddEntry(typeof(T), shouldConstruct: false);
        entry.Add(action);
    }

    public void Add<T>(Action<ByteWriter, T> writer, Func<ByteReader, T> reader)
    {
        var entry = GetOrAddEntry(typeof(T), shouldConstruct: false);
        entry.Add(GetDelegate(writer, reader));
    }

    protected static SyncWorkerDelegate<T> GetDelegate<T>(Action<ByteWriter, T> writer, Func<ByteReader, T> reader)
    {
        return (SyncWorker sync, ref T obj) =>
        {
            if (sync.isWriting)
                writer(((WritingSyncWorker)sync).writer, obj);
            else
                obj = reader(((ReadingSyncWorker)sync).reader);
        };
    }

    public SyncWorkerEntry this[Type key] {
        get {
            TryGetValue(key, out SyncWorkerEntry entry);
            return entry;
        }
    }

    public virtual IEnumerator<SyncWorkerEntry> GetEnumerator()
    {
        return explicitEntries.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public virtual bool TryGetValue(Type type, out SyncWorkerEntry syncWorkerEntry)
    {
        explicitEntries.TryGetValue(type, out syncWorkerEntry);
        return syncWorkerEntry != null;
    }

    public virtual string PrintStructure()
    {
        StringBuilder str = new StringBuilder();
        str.AppendLine("Explicit: ");
        foreach (var e in explicitEntries.Values) {
            e.PrintStructureInternal(0, str);
        }

        return str.ToString();
    }
}
