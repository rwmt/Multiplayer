using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncFieldsPatches
    {
        public static SyncField SyncAreaRestriction = Sync.Field(typeof(Pawn), "playerSettings", "AreaRestriction");
        public static SyncField SyncMedCare = Sync.Field(typeof(Pawn), "playerSettings", "medCare");
        public static SyncField SyncSelfTend = Sync.Field(typeof(Pawn), "playerSettings", "selfTend");
        public static SyncField SyncHostilityResponse = Sync.Field(typeof(Pawn), "playerSettings", "hostilityResponse");
        public static SyncField SyncFollowFieldwork = Sync.Field(typeof(Pawn), "playerSettings", "followFieldwork");
        public static SyncField SyncFollowDrafted = Sync.Field(typeof(Pawn), "playerSettings", "followDrafted");
        public static SyncField SyncMaster = Sync.Field(typeof(Pawn), "playerSettings", "master");
        public static SyncField SyncGetsFood = Sync.Field(typeof(Pawn), "guest", "GetsFood");

        public static SyncField SyncUseWorkPriorities = Sync.Field(null, "Verse.Current/Game/playSettings", "useWorkPriorities");
        public static SyncField SyncAutoHomeArea = Sync.Field(null, "Verse.Current/Game/playSettings", "autoHomeArea");
        public static SyncField[] SyncDefaultCare = Sync.Fields(
            null,
            "Verse.Find/World/settings",
            "defaultCareForColonyHumanlike",
            "defaultCareForColonyPrisoner",
            "defaultCareForColonyAnimal",
            "defaultCareForNeutralAnimal",
            "defaultCareForNeutralFaction",
            "defaultCareForHostileFaction"
        );

        [MpPatch(typeof(AreaAllowedGUI), "DoAreaSelector")]
        public static void DoAreaSelector_Prefix(Pawn p)
        {
            SyncAreaRestriction.Watch(p);
        }

        [MpPatch(typeof(PawnColumnWorker_AllowedArea), "HeaderClicked")]
        public static void AllowedArea_HeaderClicked_Prefix(PawnTable table)
        {
            foreach (Pawn pawn in table.PawnsListForReading)
                SyncAreaRestriction.Watch(pawn);
        }

        [MpPatch("RimWorld.InspectPaneFiller+<DrawAreaAllowed>c__AnonStorey0", "<>m__0")]
        public static void DrawAreaAllowed_Inner(object __instance)
        {
            SyncAreaRestriction.Watch(__instance, "pawn");
        }

        [MpPatch(typeof(HealthCardUtility), "DrawOverviewTab")]
        public static void HealthCardUtility1(Pawn pawn)
        {
            SyncMedCare.Watch(pawn);
            SyncSelfTend.Watch(pawn);
        }

        [MpPatch(typeof(ITab_Pawn_Visitor), "FillTab")]
        public static void ITab_Pawn_Visitor(ITab __instance)
        {
            SyncMedCare.Watch(__instance, "SelPawn");
            SyncGetsFood.Watch(__instance, "SelPawn");
        }

        [MpPatch(typeof(HostilityResponseModeUtility), "DrawResponseButton")]
        public static void DrawResponseButton(Pawn pawn)
        {
            SyncHostilityResponse.Watch(pawn);
        }

        [MpPatch(typeof(PawnColumnWorker_FollowFieldwork), "SetValue")]
        public static void FollowFieldwork(Pawn pawn)
        {
            SyncFollowFieldwork.Watch(pawn);
        }

        [MpPatch(typeof(PawnColumnWorker_FollowDrafted), "SetValue")]
        public static void FollowDrafted(Pawn pawn)
        {
            SyncFollowDrafted.Watch(pawn);
        }

        [MpPatch("RimWorld.TrainableUtility+<OpenMasterSelectMenu>c__AnonStorey0", "<>m__0")]
        public static void OpenMasterSelectMenu_Inner1(object __instance)
        {
            SyncMaster.Watch(__instance, "p");
        }

        [MpPatch("RimWorld.TrainableUtility+<OpenMasterSelectMenu>c__AnonStorey1", "<>m__0")]
        public static void OpenMasterSelectMenu_Inner2(object __instance)
        {
            SyncMaster.Watch(__instance, "<>f__ref$0/p");
        }

        [MpPatch(typeof(Dialog_MedicalDefaults), "DoWindowContents")]
        public static void MedicalDefaults()
        {
            SyncDefaultCare.Watch();
        }

        [MpPatch(typeof(Widgets), "CheckboxLabeled")]
        public static void CheckboxLabeled()
        {
            if (MethodMarkers.manualPriorities)
                SyncUseWorkPriorities.Watch();
        }

        [MpPatch(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
        public static void PlaySettingsControls()
        {
            SyncAutoHomeArea.Watch();
        }
    }

    public static class SyncPatches
    {
        public static SyncMethod SyncSetAssignment = Sync.Method(typeof(Pawn), "timetable", "SetAssignment", typeof(int), typeof(TimeAssignmentDef));
        public static SyncMethod SyncSetPriority = Sync.Method(typeof(Pawn), "workSettings", "SetPriority", typeof(WorkTypeDef), typeof(int));
        public static SyncField SyncTimetable = Sync.Field(typeof(Pawn), "timetable", "times");

        private static FieldInfo timetablePawn = AccessTools.Field(typeof(Pawn_TimetableTracker), "pawn");
        private static FieldInfo workPrioritiesPawn = AccessTools.Field(typeof(Pawn_WorkSettings), "pawn");
        private static FieldInfo timetableClipboard = AccessTools.Field(typeof(PawnColumnWorker_CopyPasteTimetable), "clipboard");

        [MpPatch(typeof(Pawn_TimetableTracker), "SetAssignment")]
        public static bool SetTimetableAssignment(Pawn_TimetableTracker __instance, int hour, TimeAssignmentDef ta)
        {
            if (!Multiplayer.ShouldSync) return true;

            Pawn pawn = timetablePawn.GetValue(__instance) as Pawn;
            SyncSetAssignment.DoSync(pawn, hour, ta);

            return false;
        }

        [MpPatch(typeof(PawnColumnWorker_CopyPasteTimetable), "PasteTo")]
        public static bool CopyPasteTimetable(Pawn p)
        {
            if (!Multiplayer.ShouldSync) return true;

            SyncTimetable.DoSync(p, timetableClipboard.GetValue(null));
            return false;
        }

        [MpPatch(typeof(Pawn_WorkSettings), "SetPriority")]
        public static bool SetWorkPriority(Pawn_WorkSettings __instance, WorkTypeDef w, int priority)
        {
            if (!Multiplayer.ShouldSync) return true;

            Pawn pawn = workPrioritiesPawn.GetValue(__instance) as Pawn;
            SyncSetPriority.DoSync(pawn, w, priority);

            return false;
        }
    }

    public static class MethodMarkers
    {
        public static bool manualPriorities;

        [MpPatch(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        public static void ManualPriorities_Prefix()
        {
            manualPriorities = true;
        }

        [MpPatch(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        [MpPostfix]
        public static void ManualPriorities_Postfix()
        {
            manualPriorities = false;
        }
    }

    public abstract class SyncHandler
    {
        public readonly int syncId;

        public SyncHandler(int syncId)
        {
            this.syncId = syncId;
        }
    }

    public class SyncField : SyncHandler
    {
        public readonly Type targetType;
        public readonly string memberPath;

        public readonly Type fieldType;

        public SyncField(int syncId, Type targetType, string memberPath) : base(syncId)
        {
            this.targetType = targetType;
            this.memberPath = targetType != null ? (targetType + "/" + memberPath) : memberPath;

            fieldType = MpReflection.PropertyOrFieldType(this.memberPath);
        }

        public void Watch(object target = null, string instancePath = null)
        {
            if (!Multiplayer.ShouldSync) return;

            object instance = target;
            if (instancePath != null)
                instance = MpReflection.GetPropertyOrField(target, instancePath);

            object value = MpReflection.GetPropertyOrField(instance, memberPath);
            Sync.currentMethod.Add(new SyncData(target, this, value));
        }

        public void DoSync(object target, object value)
        {
            int mapId = -1;
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(syncId);

            if (target is Pawn pawn)
            {
                mapId = pawn.Map.uniqueID;
                writer.WriteInt32(pawn.thingIDNumber);
            }

            Sync.WriteSyncObject(writer, value, fieldType);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());
        }
    }

    public class SyncMethod : SyncHandler
    {
        public readonly Type targetType;
        public readonly string instancePath;
        public readonly string methodName;
        public readonly Type[] argTypes;

        public readonly MethodInfo method;

        public SyncMethod(int syncId, Type targetType, string instancePath, string methodName, Type[] argTypes) : base(syncId)
        {
            this.targetType = targetType;
            this.instancePath = targetType + "/" + instancePath;
            this.methodName = methodName;
            this.argTypes = argTypes;

            Type instanceType = MpReflection.PropertyOrFieldType(this.instancePath);
            method = AccessTools.Method(instanceType, methodName);
        }

        public void DoSync(object target, params object[] args)
        {
            int mapId = -1;
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(syncId);

            if (target is Pawn pawn)
            {
                mapId = pawn.Map.uniqueID;
                writer.WriteInt32(pawn.thingIDNumber);
            }

            for (int i = 0; i < args.Length; i++)
                Sync.WriteSyncObject(writer, args[i], argTypes[i]);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());
        }
    }

    public struct SyncData
    {
        public readonly object target;
        public readonly SyncField handler;
        public readonly object value;

        public SyncData(object instance, SyncField handler, object value)
        {
            this.target = instance;
            this.handler = handler;
            this.value = value;
        }
    }

    public static class Sync
    {
        private static List<SyncHandler> handlers = new List<SyncHandler>();

        public static List<SyncData> currentMethod = new List<SyncData>();
        public static bool syncing;

        private static void Prefix(ref bool __state)
        {
            if (!syncing && Multiplayer.ShouldSync)
                syncing = __state = true;
        }

        private static void Postfix(ref bool __state)
        {
            if (!__state)
                return;

            foreach (SyncData data in currentMethod)
            {
                object newValue = MpReflection.GetPropertyOrField(data.target, data.handler.memberPath);

                if (!Equals(newValue, data.value))
                {
                    MpReflection.SetPropertyOrField(data.target, data.handler.memberPath, data.value);
                    data.handler.DoSync(data.target, newValue);
                }
            }

            currentMethod.Clear();
            syncing = __state = false;
        }

        public static SyncMethod Method(Type targetType, string instancePath, string methodName, params Type[] argTypes)
        {
            SyncMethod handler = new SyncMethod(handlers.Count, targetType, instancePath, methodName, argTypes);
            handlers.Add(handler);
            return handler;
        }

        public static SyncField Field(Type targetType, string instancePath, string fieldName)
        {
            SyncField handler = new SyncField(handlers.Count, targetType, instancePath + "/" + fieldName);
            handlers.Add(handler);
            return handler;
        }

        public static SyncField[] Fields(Type targetType, string instancePath, params string[] memberPaths)
        {
            return memberPaths.Select(path => Field(targetType, instancePath, path)).ToArray();
        }

        public static void RegisterPatches(Type type)
        {
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(Sync), "Prefix"));
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(Sync), "Postfix"));

            List<MethodBase> patched = MpPatch.DoPatches(type);
            new PatchProcessor(Multiplayer.harmony, patched, prefix, postfix).Patch();
        }

        public static void Watch(this SyncField[] group, object target = null)
        {
            foreach (SyncField sync in group)
                sync.Watch(target);
        }

        public static void HandleCmd(ByteReader data)
        {
            int syncId = data.ReadInt32();
            SyncHandler handler = handlers[syncId];

            if (handler is SyncMethod method)
            {
                object target = ReadTarget(data, method.targetType);
                target = MpReflection.GetPropertyOrField(target, method.instancePath);
                object[] parameters = ReadSyncObjects(data, method.argTypes);

                MpLog.Log("Invoked " + method.method + " on " + target + " with " + parameters.Length + " params");
                method.method.Invoke(target, parameters);
            }
            else if (handler is SyncField field)
            {
                object target = ReadTarget(data, field.targetType);
                object value = ReadSyncObject(data, field.fieldType);

                MpLog.Log("Set " + field.memberPath + " in " + target + " to " + value + " map " + data.context);
                MpReflection.SetPropertyOrField(target, field.memberPath, value);
            }
        }

        private static object ReadTarget(ByteReader data, Type targetType)
        {
            object target = null;
            if (targetType == typeof(Pawn))
            {
                int pawnId = data.ReadInt32();
                Map map = data.context as Map;
                target = map.mapPawns.AllPawns.FirstOrDefault(p => p.thingIDNumber == pawnId);
            }

            return target;
        }

        static ReaderDictionary readers = new ReaderDictionary
        {
            { data => data.ReadInt32() },
            { data => data.ReadBool() },
            { data => data.ReadString() },
            { data => new IntVec3(data.ReadInt32(), data.ReadInt32(), data.ReadInt32()) }
        };

        static WriterDictionary writers = new WriterDictionary
        {
            { (ByteWriter data, int o) => data.WriteInt32(o) },
            { (ByteWriter data, bool o) => data.WriteBool(o) },
            { (ByteWriter data, string o) => data.Write(o) },
            {
                (ByteWriter data, IntVec3 vec) =>
                {
                    data.WriteInt32(vec.x);
                    data.WriteInt32(vec.y);
                    data.WriteInt32(vec.z);
                }
            }
        };

        public static List<T> ReadList<T>(ByteReader data)
        {
            int size = data.ReadInt32();
            return null;
        }

        public static object ReadSyncObject(ByteReader data, Type type)
        {
            if (type.IsEnum)
            {
                return Enum.ToObject(type, data.ReadInt32());
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type listType = type.GetGenericArguments()[0];
                int size = data.ReadInt32();
                IList list = Activator.CreateInstance(type, size) as IList;
                for (int j = 0; j < size; j++)
                    list.Add(ReadSyncObject(data, listType));
                return list;
            }
            else if (typeof(Area).IsAssignableFrom(type))
            {
                int areaId = data.ReadInt32();
                if (areaId == -1)
                {
                    return null;
                }

                Map map = data.context as Map;
                return map.areaManager.AllAreas.FirstOrDefault(a => a.ID == areaId);
            }
            else if (typeof(Def).IsAssignableFrom(type))
            {
                ushort shortHash = data.ReadUInt16();
                if (shortHash == 0)
                {
                    return null;
                }

                Type dbType = typeof(DefDatabase<>).MakeGenericType(type);
                return AccessTools.Method(dbType, "GetByShortHash").Invoke(null, new object[] { shortHash });
            }
            else if (readers.TryGetValue(type, out Func<ByteReader, object> reader))
            {
                return reader(data);
            }
            else
            {
                throw new Exception("Couldn't read type " + type);
            }
        }

        public static object[] ReadSyncObjects(ByteReader data, params Type[] spec)
        {
            object[] read = new object[spec.Length];

            for (int i = 0; i < spec.Length; i++)
            {
                Type type = spec[i];
                read[i] = ReadSyncObject(data, type);
            }

            return read;
        }

        public static void WriteSyncObject(ByteWriter data, object obj, Type type)
        {
            if (type.IsEnum)
            {
                data.WriteInt32(Convert.ToInt32(obj));
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type listType = type.GetGenericArguments()[0];
                IList list = obj as IList;
                data.WriteInt32(list.Count);
                foreach (object e in list)
                    WriteSyncObject(data, e, listType);
            }
            else if (typeof(Area).IsAssignableFrom(type))
            {
                data.WriteInt32(obj is Area area ? area.ID : -1);
            }
            else if (typeof(Def).IsAssignableFrom(type))
            {
                data.WriteUInt16(obj is Def def ? def.shortHash : (ushort)0);
            }
            else if (writers.TryGetValue(type, out Action<ByteWriter, object> writer))
            {
                writer(data, obj);
            }
            else
            {
                throw new Exception("Couldn't write type " + type);
            }
        }
    }

    class ReaderDictionary : OrderedDictionary<Type, Func<ByteReader, object>>
    {
        public void Add<T>(Func<ByteReader, T> writer)
        {
            Add(typeof(T), data => writer(data));
        }
    }

    class WriterDictionary : OrderedDictionary<Type, Action<ByteWriter, object>>
    {
        public void Add<T>(Action<ByteWriter, T> writer)
        {
            Add(typeof(T), (data, o) => writer(data, (T)o));
        }
    }

    abstract class OrderedDictionary<K, V> : IEnumerable
    {
        private OrderedDictionary dict = new OrderedDictionary();

        protected void Add(K key, V value)
        {
            dict.Add(key, value);
        }

        public bool TryGetValue(K key, out V value)
        {
            value = (V)dict[key];
            return value != null;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return dict.GetEnumerator();
        }
    }

}
