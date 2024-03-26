using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Multiplayer.API;
using Multiplayer.Common;
using Multiplayer.Common.Util;

namespace Multiplayer.Client;

public class SyncWorkerDictionaryTree : SyncWorkerDictionary
{
    protected readonly List<SyncWorkerEntry> implicitEntries = [];
    protected readonly List<SyncWorkerEntry> interfaceEntries = [];

    public SyncWorkerEntry GetOrAddEntry(Type type, bool isImplicit = false, bool shouldConstruct = false)
    {
        if (explicitEntries.TryGetValue(type, out SyncWorkerEntry explicitEntry)) {
            return explicitEntry;
        }

        if (!isImplicit) {
            return AddExplicit(type, shouldConstruct);
        }

        if (type.IsInterface) {
            var interfaceEntry = interfaceEntries.FirstOrDefault(i => i.type == type);

            if (interfaceEntry == null) {
                interfaceEntry = new SyncWorkerEntry(type, shouldConstruct);
                interfaceEntries.Add(interfaceEntry);
            }

            return interfaceEntry;
        }

        var entry = implicitEntries.FirstOrDefault(i => i.type == type);
        Stack<SyncWorkerEntry> toRemove = new Stack<SyncWorkerEntry>();

        foreach (var e in implicitEntries) {

            if (type.IsAssignableFrom(e.type) || e.type.IsAssignableFrom(type)) {
                if (entry != null) {
                    entry.Add(e);
                    toRemove.Push(e);
                    continue;
                }
                entry = e.Add(type, shouldConstruct);
            }
        }

        if (entry == null) {
            entry = new SyncWorkerEntry(type, shouldConstruct);
            implicitEntries.Add(entry);
            return entry;
        }

        foreach (var e in toRemove) {
            implicitEntries.Remove(e);
        }

        return entry;
    }

    public void Add<T>(SyncWorkerDelegate<T> action, bool isImplicit = false, bool shouldConstruct = false)
    {
        var entry = GetOrAddEntry(typeof(T), isImplicit: isImplicit, shouldConstruct: shouldConstruct);
        entry.Add(action);
    }

    public void Add<T>(Action<ByteWriter, T> writer, Func<ByteReader, T> reader, bool isImplicit = false, bool shouldConstruct = false)
    {
        var entry = GetOrAddEntry(typeof(T), isImplicit: isImplicit, shouldConstruct: shouldConstruct);
        entry.Add(GetDelegate(writer, reader));
    }

    public override bool TryGetValue(Type type, out SyncWorkerEntry syncWorkerEntry)
    {
        if (explicitEntries.TryGetValue(type, out syncWorkerEntry))
            return true;

        foreach (var e in implicitEntries) {
            syncWorkerEntry = e.GetClosest(type);

            if (syncWorkerEntry != null)
                return true;
        }

        foreach (var e in interfaceEntries) {
            syncWorkerEntry = e.GetClosest(type);

            if (syncWorkerEntry != null)
                return true;
        }

        return false;
    }

    public override IEnumerator<SyncWorkerEntry> GetEnumerator()
    {
        return explicitEntries.Values.Union(implicitEntries).Union(interfaceEntries).GetEnumerator();
    }

    public override string PrintStructure()
    {
        StringBuilder str = new StringBuilder();
        str.AppendLine("Explicit: ");
        foreach (var e in explicitEntries.Values) {
            e.PrintStructureInternal(0, str);
        }
        str.AppendLine();
        str.AppendLine("Implicit: ");
        foreach (var e in implicitEntries) {
            e.PrintStructureInternal(0, str);
        }
        str.AppendLine();
        str.AppendLine("Interface: ");
        foreach (var e in interfaceEntries) {
            e.PrintStructureInternal(0, str);
        }

        return str.ToString();
    }

    public static SyncWorkerDictionaryTree Merge(params SyncWorkerDictionaryTree[] trees)
    {
        var tree = new SyncWorkerDictionaryTree();

        foreach (var t in trees) {
            tree.explicitEntries.AddRange(t.explicitEntries);
            tree.implicitEntries.AddRange(t.implicitEntries);
            tree.interfaceEntries.AddRange(t.interfaceEntries);
        }

        return tree;
    }
}
