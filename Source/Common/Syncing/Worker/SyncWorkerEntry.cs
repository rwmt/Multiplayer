using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Multiplayer.API;

namespace Multiplayer.Client;

public class SyncWorkerEntry
{
    delegate bool SyncWorkerDelegate(SyncWorker sync, ref object? obj);

    public Type type;
    public bool shouldConstruct;
    private List<SyncWorkerDelegate> syncWorkers;
    private List<SyncWorkerEntry> subclasses = [];
    private SyncWorkerEntry? parent;

    public int SyncWorkerCount => syncWorkers.Count;

    public SyncWorkerEntry(Type type, bool shouldConstruct = false)
    {
        this.type = type;
        syncWorkers = new List<SyncWorkerDelegate>();
        this.shouldConstruct = shouldConstruct;
    }

    public SyncWorkerEntry(SyncWorkerEntry other)
    {
        type = other.type;
        syncWorkers = other.syncWorkers;
        subclasses = other.subclasses;
        shouldConstruct = other.shouldConstruct;

        foreach (var sub in subclasses)
            sub.parent = this;
    }

    public void Add(MethodInfo method)
    {
        // todo: Find a way to do this without DynDelegate
        Add(DynDelegate.DynamicDelegate.Create<SyncWorkerDelegate>(method), method.ReturnType == typeof(void));
    }

    public void Add<T>(SyncWorkerDelegate<T> func)
    {
        Add((SyncWorker sync, ref object? obj) => {
            var obj2 = (T?) obj;
            func(sync, ref obj2);
            obj = obj2;
            return true;
        });
    }

    private void Add(SyncWorkerDelegate sync, bool append = true)
    {
        if (append)
            syncWorkers.Add(sync);
        else
            syncWorkers.Insert(0, sync);
    }

    public bool Invoke(SyncWorker worker, ref object? obj)
    {
        parent?.Invoke(worker, ref obj);

        for (int i = 0; i < syncWorkers.Count; i++) {
            if (syncWorkers[i](worker, ref obj))
                return true;

            if (worker is ReadingSyncWorker reader) {
                reader.Reset();
            } else if (worker is WritingSyncWorker writer) {
                writer.Reset();
            }
        }

        return false;
    }

    public void Add(SyncWorkerEntry other)
    {
        SyncWorkerEntry newEntry = Add(other.type, other, other.shouldConstruct);
        newEntry.subclasses = other.subclasses;
    }

    public SyncWorkerEntry? Add(Type toAddType, bool toAddConstruct = false)
    {
        return Add(toAddType, null, toAddConstruct);
    }

    private SyncWorkerEntry? Add(Type toAddType, SyncWorkerEntry? toAddParent, bool toAddConstruct)
    {
        if (toAddType == type) {
            if (toAddConstruct) {
                shouldConstruct = true;
            }

            return this;
        }

        if (toAddType.IsAssignableFrom(type)) // New is parent
        {
            if (toAddParent != null) {
                List<SyncWorkerEntry> parentSubclasses = toAddParent.subclasses;
                SyncWorkerEntry newEntry = new SyncWorkerEntry(toAddType, toAddConstruct);

                parent = newEntry;
                newEntry.subclasses.Add(this);

                parentSubclasses[parentSubclasses.IndexOf(this)] = newEntry;
                return newEntry;
            } else
            {
                // Copy this into new child entry
                SyncWorkerEntry newEntry = new SyncWorkerEntry(this);

                // Make this into parent
                type = toAddType;
                shouldConstruct = toAddConstruct;
                syncWorkers = [];
                subclasses = [newEntry];
                newEntry.parent = this;

                return this;
            }
        }

        if (type.IsAssignableFrom(toAddType)) // New is child
        {
            // Try add as child of a child
            for (int i = 0; i < subclasses.Count; i++) {
                SyncWorkerEntry? res = subclasses[i].Add(toAddType, this, toAddConstruct);
                if (res != null)
                    return res;
            }

            // Make into new child of this
            var newEntry = new SyncWorkerEntry(toAddType, toAddConstruct)
            {
                parent = this
            };
            subclasses.Add(newEntry);

            return newEntry;
        }

        return null;
    }

    public SyncWorkerEntry? GetClosest(Type queryType)
    {
        if (type.IsAssignableFrom(queryType)) {
            int len = subclasses.Count;

            for (int i = 0; i < len; i++) {
                SyncWorkerEntry? res = subclasses[i].GetClosest(queryType);

                if (res != null)
                    return res;
            }

            return this;
        }

        return null;
    }

    internal void PrintStructureInternal(int level, StringBuilder str)
    {
        str.Append(' ', 4 * level);
        str.Append(type);

        if (subclasses.Count == 0) {
            str.AppendLine();
            return;
        }

        str.AppendLine(" ┓ ");

        for (int i = 0; i < subclasses.Count; i++)
            subclasses[i].PrintStructureInternal(level + 1, str);
    }
}
