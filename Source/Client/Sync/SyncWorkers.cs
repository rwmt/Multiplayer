using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using Multiplayer.API;
using Multiplayer.Common;

using RimWorld;
using RimWorld.Planet;

using Verse;

namespace Multiplayer.Client
{
    public class SyncWorkerEntry
    {
        delegate bool SyncWorkerDelegate(SyncWorker sync, ref object obj);

        public Type type;

        public bool shouldConstruct;

        private List<SyncWorkerDelegate> syncWorkers;

        private List<SyncWorkerEntry> subclasses;

        private SyncWorkerEntry parent;

        public int SyncWorkerCount => syncWorkers.Count();

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
            // todo: Find a way to do this without DynDelegate
            Add(DynDelegate.DynamicDelegate.Create<SyncWorkerDelegate>(method), method.ReturnType == typeof(void));
        }

        public void Add<T>(SyncWorkerDelegate<T> func)
        {
            Add(new SyncWorkerDelegate((SyncWorker sync, ref object obj) => {
                var obj2 = (T) obj;
                func(sync, ref obj2);
                obj = obj2;
                return true;
            }), true);
        }

        void Add(SyncWorkerDelegate sync, bool append = true)
        {
            if (append)
                syncWorkers.Add(sync);
            else
                syncWorkers.Insert(0, sync);
        }

        public bool Invoke(SyncWorker worker, ref object obj)
        {
            if (parent != null) {
                parent.Invoke(worker, ref obj);
            }

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

        public SyncWorkerEntry Add(Type type, bool shouldConstruct = false)
        {
            return Add(type, null, shouldConstruct);
        }

        private SyncWorkerEntry Add(Type type, SyncWorkerEntry parent, bool shouldConstruct)
        {
            if (type == this.type) {
                if (shouldConstruct) {
                    this.shouldConstruct = true;
                }

                return this;
            }

            if (type.IsAssignableFrom(this.type))   // Is parent
            {
                SyncWorkerEntry newEntry;

                if (parent != null) {
                    List<SyncWorkerEntry> ps = parent.subclasses;
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
                        SyncWorkerEntry res = subclasses[i].Add(type, this, shouldConstruct);
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

        public SyncWorkerEntry GetClosest(Type type)
        {
            if (this.type.IsAssignableFrom(type)) {

                if (subclasses == null)
                    return this;

                int len = subclasses.Count;

                if (len == 0)
                    return this;

                for (int i = 0; i < len; i++) {
                    SyncWorkerEntry res = subclasses[i].GetClosest(type);

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
            str.Append(type.ToString());

            if (subclasses == null) {
                str.AppendLine();
                return;
            }

            str.AppendLine(" ┓ ");

            for (int i = 0; i < subclasses.Count; i++)
                subclasses[i].PrintStructureInternal(level + 1, str);
        }
    }

    class SyncWorkerDictionary : IEnumerable<SyncWorkerEntry>
    {
        protected readonly Dictionary<Type, SyncWorkerEntry> explicitEntries = new Dictionary<Type, SyncWorkerEntry>();

        public SyncWorkerEntry GetOrAddEntry(Type type, bool shouldConstruct = false)
        {
            if (explicitEntries.TryGetValue(type, out SyncWorkerEntry explicitEntry)) {
                return explicitEntry;
            }

            return AddExplicit(type, shouldConstruct);
        }

        protected SyncWorkerEntry AddExplicit(Type type, bool shouldConstruct = false)
        {
            var explicitEntry = new SyncWorkerEntry(type, shouldConstruct);

            explicitEntries.Add(type, explicitEntry);

            return explicitEntry;
        }

        internal void Add<T>(SyncWorkerDelegate<T> action)
        {
            var entry = GetOrAddEntry(typeof(T), shouldConstruct: false);

            entry.Add(action);
        }

        internal void Add<T>(Action<ByteWriter, T> writer, Func<ByteReader, T> reader)
        {
            var entry = GetOrAddEntry(typeof(T), shouldConstruct: false);

            AddDelegate(entry, writer, reader);
        }

        protected static void AddDelegate<T>(SyncWorkerEntry entry, Action<ByteWriter, T> writer, Func<ByteReader, T> reader)
        {
            entry.Add((SyncWorker sync, ref T obj) => {
                if (sync.isWriting) {
                    writer(((WritingSyncWorker) sync).writer, obj);
                } else {
                    obj = reader(((ReadingSyncWorker) sync).reader);
                }
            });
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

            if (syncWorkerEntry != null)
                return true;
                
            return false;
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

    class SyncWorkerDictionaryTree : SyncWorkerDictionary
    {
        protected readonly List<SyncWorkerEntry> implicitEntries = new List<SyncWorkerEntry>();
        protected readonly List<SyncWorkerEntry> interfaceEntries = new List<SyncWorkerEntry>();

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

        internal void Add<T>(SyncWorkerDelegate<T> action, bool isImplicit = false, bool shouldConstruct = false)
        {
            var entry = GetOrAddEntry(typeof(T), isImplicit: isImplicit, shouldConstruct: shouldConstruct);

            entry.Add(action);
        }

        internal void Add<T>(Action<ByteWriter, T> writer, Func<ByteReader, T> reader, bool isImplicit = false, bool shouldConstruct = false)
        {
            var entry = GetOrAddEntry(typeof(T), isImplicit: isImplicit, shouldConstruct: shouldConstruct);

            AddDelegate(entry, writer, reader);
        }

        public override bool TryGetValue(Type type, out SyncWorkerEntry syncWorkerEntry)
        {
            explicitEntries.TryGetValue(type, out syncWorkerEntry);

            if (syncWorkerEntry != null)
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
            str.AppendLine("Interface: ");
            foreach (var e in interfaceEntries) {
                e.PrintStructureInternal(0, str);
            }
            str.AppendLine();
            str.AppendLine("Implicit: ");
            foreach (var e in implicitEntries) {
                e.PrintStructureInternal(0, str);
            }

            return str.ToString();
        }
    }

    internal static class TypeRWHelper
    {
        private static Dictionary<Type, Type[]> cache = new Dictionary<Type, Type[]>();

        static TypeRWHelper()
        {
            cache[typeof(IStoreSettingsParent)] = Sync.storageParents;
            cache[typeof(IPlantToGrowSettable)] = Sync.plantToGrowSettables;

            cache[typeof(ThingComp)] = Sync.thingCompTypes;
            cache[typeof(Designator)] = Sync.designatorTypes;
            cache[typeof(WorldObjectComp)] = Sync.worldObjectCompTypes;

            cache[typeof(GameComponent)] = Sync.gameCompTypes;
            cache[typeof(WorldComponent)] = Sync.worldCompTypes;
            cache[typeof(MapComponent)] = Sync.mapCompTypes;
        }

        internal static void FlushCache()
        {
            cache.Clear();
        }

        internal static Type[] GenTypeCache(Type type)
        {
            var types = GenTypes.AllTypes
                .Where(t => t != type && type.IsAssignableFrom(t))
                .OrderBy(t => t.IsInterface)
                .ToArray();

            cache[type] = types;
            return types;
        }

        internal static Type GetType(ushort index, Type baseType)
        {
            if (!cache.TryGetValue(baseType, out Type[] types))
                types = GenTypeCache(baseType);

            return types[index];
        }

        internal static ushort GetTypeIndex(Type type, Type baseType)
        {
            if (!cache.TryGetValue(baseType, out Type[] types))
                types = GenTypeCache(baseType);

            return (ushort) types.FindIndex(type);
        }
    }

    public class WritingSyncWorker : SyncWorker
    {
        internal readonly ByteWriter writer;
        readonly int initialPos;

        public WritingSyncWorker(ByteWriter writer) : base(true)
        {
            this.writer = writer;
            initialPos = writer.Position;
        }

        public override void Bind<T>(ref T obj)
        {
            Sync.WriteSyncObject(writer, obj, typeof(T));
        }

        public override void Bind(object obj, string name)
        {
            object value = MpReflection.GetValue(obj, name);
            Type type = value.GetType();

            Sync.WriteSyncObject(writer, value, type);
        }

        public override void Bind(ref byte obj)
        {
            writer.WriteByte(obj);
        }

        public override void Bind(ref sbyte obj)
        {
            writer.WriteSByte(obj);
        }

        public override void Bind(ref short obj)
        {
            writer.WriteShort(obj);
        }

        public override void Bind(ref ushort obj)
        {
            writer.WriteUShort(obj);
        }

        public override void Bind(ref int obj)
        {
            writer.WriteInt32(obj);
        }

        public override void Bind(ref uint obj)
        {
            writer.WriteUInt32(obj);
        }

        public override void Bind(ref long obj)
        {
            writer.WriteLong(obj);
        }

        public override void Bind(ref ulong obj)
        {
            writer.WriteULong(obj);
        }

        public override void Bind(ref float obj)
        {
            writer.WriteFloat(obj);
        }

        public override void Bind(ref double obj)
        {
            writer.WriteDouble(obj);
        }

        public override void Bind(ref bool obj)
        {
            writer.WriteBool(obj);
        }

        public override void Bind(ref string obj)
        {
            writer.WriteString(obj);
        }

        public override void BindType<T>(ref Type type)
        {
            writer.WriteUShort(TypeRWHelper.GetTypeIndex(type, typeof(T)));
        }

        internal void Reset()
        {
            writer.SetLength(initialPos);
        }
    }

    public class ReadingSyncWorker : SyncWorker
    {
        internal readonly ByteReader reader;
        readonly int initialPos;

        public ReadingSyncWorker(ByteReader reader) : base(false)
        {
            this.reader = reader;
            initialPos = reader.Position;
        }

        public override void Bind<T>(ref T obj)
        {
            obj = (T) Sync.ReadSyncObject(reader, typeof(T));
        }

        public override void Bind(object obj, string name)
        {
            object value = MpReflection.GetValue(obj, name);

            Type type = value.GetType();

            var res = Sync.ReadSyncObject(reader, type);

            MpReflection.SetValue(obj, name, res);
        }

        public override void Bind(ref byte obj)
        {
            obj = reader.ReadByte();
        }

        public override void Bind(ref sbyte obj)
        {
            obj = reader.ReadSByte();
        }

        public override void Bind(ref short obj)
        {
            obj = reader.ReadShort();
        }

        public override void Bind(ref ushort obj)
        {
            obj = reader.ReadUShort();
        }

        public override void Bind(ref int obj)
        {
            obj = reader.ReadInt32();
        }

        public override void Bind(ref uint obj)
        {
            obj = reader.ReadUInt32();
        }

        public override void Bind(ref long obj)
        {
            obj = reader.ReadLong();
        }

        public override void Bind(ref ulong obj)
        {
            obj = reader.ReadULong();
        }

        public override void Bind(ref float obj)
        {
            obj = reader.ReadFloat();
        }

        public override void Bind(ref double obj)
        {
            obj = reader.ReadDouble();
        }

        public override void Bind(ref bool obj)
        {
            obj = reader.ReadBool();
        }

        public override void Bind(ref string obj)
        {
            obj = reader.ReadString();
        }

        public override void BindType<T>(ref Type type)
        {
            type = TypeRWHelper.GetType(reader.ReadUShort(), typeof(T));
        }

        internal void Reset()
        {
            reader.Seek(initialPos);
        }
    }
}
