using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Multiplayer.API;

namespace Multiplayer.Client;

public class SyncWorkerEntry
{
    delegate bool SyncWorkerDelegate(SyncWorker sync, ref object? obj);
    delegate void SyncWorkerDelegateNoReturn(SyncWorker sync, ref object? obj);

    public Type type;
    public bool shouldConstruct;
    private List<SyncWorkerDelegate> syncWorkers;
    private List<SyncWorkerEntry>? subclasses;
    private SyncWorkerEntry? parent;

    public int SyncWorkerCount => syncWorkers.Count;

    public SyncWorkerEntry(Type type, bool shouldConstruct = false)
    {
        this.type = type;
        this.syncWorkers = new List<SyncWorkerDelegate>();
        this.shouldConstruct = shouldConstruct;
    }

    public SyncWorkerEntry(SyncWorkerEntry other)
    {
        type = other.type;
        syncWorkers = other.syncWorkers;
        subclasses = other.subclasses;
        shouldConstruct = other.shouldConstruct;
    }

    public void Add(MethodInfo method)
    {
        if (method.ReturnType == typeof(void))
        {
            var func = (SyncWorkerDelegateNoReturn)Delegate.CreateDelegate(typeof(SyncWorkerDelegateNoReturn), method);
            Add((SyncWorker sync, ref object obj) =>
            {
                func(sync, ref obj);
                return true;
            }, true);
        }
        else
        {
            Add((SyncWorkerDelegate)Delegate.CreateDelegate(typeof(SyncWorkerDelegate), method), false);
        }
    }

    public void Add<T>(SyncWorkerDelegate<T> func)
    {
        Add((SyncWorker sync, ref object? obj) => {
            var obj2 = (T?) obj;
            func(sync, ref obj2);
            obj = obj2;
            return true;
        }, true);
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

    public SyncWorkerEntry Add(SyncWorkerEntry other)
    {
        SyncWorkerEntry newEntry = Add(other.type, other, other.shouldConstruct);
        newEntry.subclasses = other.subclasses;
        return newEntry;
    }

    public SyncWorkerEntry? Add(Type type, bool shouldConstruct = false)
    {
        return Add(type, null, shouldConstruct);
    }

    private SyncWorkerEntry? Add(Type type, SyncWorkerEntry? parent, bool shouldConstruct)
    {
        if (type == this.type) {
            if (shouldConstruct) {
                this.shouldConstruct = true;
            }

            return this;
        }

        if (type.IsAssignableFrom(this.type)) // Is parent
        {
            SyncWorkerEntry newEntry;

            if (parent != null) {
                List<SyncWorkerEntry>? ps = parent.subclasses;
                newEntry = new SyncWorkerEntry(type, shouldConstruct);

                newEntry.subclasses.Add(this);

                ps[ps.IndexOf(this)] = newEntry;
                return newEntry;
            } else {
                newEntry = new SyncWorkerEntry(this);

                this.type = type;
                this.shouldConstruct = shouldConstruct;
                syncWorkers = new List<SyncWorkerDelegate>();
                subclasses = new List<SyncWorkerEntry>() { newEntry };

                return this;
            }
        }

        if (this.type.IsAssignableFrom(type)) // Is child
        {
            if (subclasses != null) {
                for (int i = 0; i < subclasses.Count; i++) {
                    SyncWorkerEntry? res = subclasses[i].Add(type, this, shouldConstruct);
                    if (res != null)
                        return res;
                }
            } else {
                subclasses = new List<SyncWorkerEntry>();
            }

            var newEntry = new SyncWorkerEntry(type, shouldConstruct);
            newEntry.parent = this;
            subclasses.Add(newEntry);

            return newEntry;
        }

        return null;
    }

    public SyncWorkerEntry? GetClosest(Type queryType)
    {
        if (type.IsAssignableFrom(queryType)) {
            if (subclasses == null)
                return this;

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

        if (subclasses == null) {
            str.AppendLine();
            return;
        }

        str.AppendLine(" ┓ ");

        for (int i = 0; i < subclasses.Count; i++)
            subclasses[i].PrintStructureInternal(level + 1, str);
    }
}
