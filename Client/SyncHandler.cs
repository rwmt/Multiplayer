using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
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
        public static SyncField SyncInteractionMode = Sync.Field(typeof(Pawn), "guest", "interactionMode");

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
            SyncInteractionMode.Watch(__instance, "SelPawn");
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

        [MpPatch(typeof(TrainableUtility), "<OpenMasterSelectMenu>c__AnonStorey0", "<>m__0")]
        public static void OpenMasterSelectMenu_Inner1(object __instance)
        {
            SyncMaster.Watch(__instance, "p");
        }

        [MpPatch(typeof(TrainableUtility), "<OpenMasterSelectMenu>c__AnonStorey1", "<>m__0")]
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

    [StaticConstructorOnStartup]
    public static class SyncPatches
    {
        public static SyncMethod SyncSetAssignment = Sync.Method(typeof(Pawn), "timetable", "SetAssignment");
        public static SyncMethod SyncSetPriority = Sync.Method(typeof(Pawn), "workSettings", "SetPriority");
        public static SyncMethod SyncSetDrafted = Sync.Method(typeof(Pawn), "drafter", "set_Drafted");
        public static SyncMethod SyncSetFireAtWill = Sync.Method(typeof(Pawn), "drafter", "set_FireAtWill");

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

        [MpPatch(typeof(Pawn_DraftController), "set_Drafted")]
        public static bool SetDrafted(Pawn_DraftController __instance, bool value)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncSetDrafted.DoSync(__instance.pawn, value);
            return false;
        }

        [MpPatch(typeof(Pawn_DraftController), "set_FireAtWill")]
        public static bool SetFireAtWill(Pawn_DraftController __instance, bool value)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncSetFireAtWill.DoSync(__instance.pawn, value);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<GotoLocationOption>c__AnonStorey18", "<>m__0")]
        public static bool FloatMenuGoto(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_PAWN);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<AddDraftedOrders>c__AnonStorey3", "<>m__0")]
        public static bool FloatMenuArrest(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_PAWN);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__0")]
        public static bool FloatMenuRescue(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_PAWN);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__1")]
        public static bool FloatMenuCapture(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_PAWN);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey9", "<>m__0")]
        public static bool FloatMenuCarryToCryptosleepCasket(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_PAWN);
            return false;
        }

        [SyncDelegate("$this")]
        [MpPatch(typeof(Pawn_PlayerSettings), "<GetGizmos>c__Iterator0", "<>m__2")]
        public static bool GizmoReleaseAnimals(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, __instance.GetPropertyOrField("$this/pawn"));
            return false;
        }

        [SyncDelegate("$this")]
        [MpPatch(typeof(PriorityWork), "<GetGizmos>c__Iterator0", "<>m__0")]
        public static bool GizmoClearPrioritizedWork(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, __instance.GetPropertyOrField("$this/pawn"));
            return false;
        }

        /*[SyncDelegate("lord")]
        [MpPatch(typeof(Pawn_MindState), "<GetGizmos>c__Iterator0+<GetGizmos>c__AnonStorey2", "<>m__0")]
        public static bool GizmoCancelFormingCaravan(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MpReflection.GetPropertyOrField(__instance, ""));
            return false;
        }*/
    }

    public static class MapProviderMode
    {
        public static readonly object ANY_PAWN = new object();
    }

    public class SyncDelegateAttribute : Attribute
    {
        public readonly string[] fields;

        public SyncDelegateAttribute()
        {
        }

        public SyncDelegateAttribute(params string[] fields)
        {
            this.fields = fields;
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

        public abstract void Read(ByteReader data);
    }

    public class SyncField : SyncHandler
    {
        public readonly Type targetType;
        public readonly string memberPath;

        public readonly Type fieldType;

        public SyncField(int syncId, Type targetType, string memberPath) : base(syncId)
        {
            this.targetType = targetType;
            this.memberPath = targetType + "/" + memberPath;

            fieldType = MpReflection.PropertyOrFieldType(this.memberPath);
        }

        public void Watch(object target = null, string targetPath = null)
        {
            if (!Multiplayer.ShouldSync) return;

            if (targetPath != null)
                target = target.GetPropertyOrField(targetPath);

            object value = target.GetPropertyOrField(memberPath);
            Sync.currentMethod.Add(new SyncData(target, this, value));
        }

        public void DoSync(object target, object value)
        {
            int mapId = Sync.GetMapId(target);
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(syncId);

            Sync.WriteContext(writer, mapId);

            if (target is Pawn p)
                writer.WriteInt32(p.thingIDNumber);

            Sync.WriteSyncObject(writer, value, fieldType);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());
        }

        public override void Read(ByteReader data)
        {
            object target = Sync.ReadTarget(data, targetType);
            object value = Sync.ReadSyncObject(data, fieldType);

            MpLog.Log("Set " + memberPath + " in " + target + " to " + value + " map " + data.context);
            MpReflection.SetPropertyOrField(target, memberPath, value);
        }
    }

    public class SyncMethod : SyncHandler
    {
        public readonly Type targetType;
        public readonly string instancePath;

        public readonly MethodInfo method;
        public readonly Type[] argTypes;

        public SyncMethod(int syncId, Type targetType, string instancePath, string methodName) : base(syncId)
        {
            this.targetType = targetType;

            Type instanceType = targetType;
            if (!instancePath.NullOrEmpty())
            {
                this.instancePath = instanceType + "/" + instancePath;
                instanceType = MpReflection.PropertyOrFieldType(this.instancePath);
            }

            method = AccessTools.Method(instanceType, methodName);
            argTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
        }

        public void DoSync(object target, params object[] args)
        {
            int mapId = Sync.GetMapId(target);
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(syncId);

            Sync.WriteContext(writer, mapId);

            if (target is Pawn p)
                writer.WriteInt32(p.thingIDNumber);

            Sync.WriteSyncObjects(writer, args, argTypes);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());
        }

        public override void Read(ByteReader data)
        {
            object target = Sync.ReadTarget(data, targetType);
            if (!instancePath.NullOrEmpty())
                target = target.GetPropertyOrField(instancePath);

            object[] parameters = Sync.ReadSyncObjects(data, argTypes);

            MpLog.Log("Invoked " + method + " on " + target + " with " + parameters.Length + " params");
            method.Invoke(target, parameters);
        }
    }

    public class SyncDelegate : SyncHandler
    {
        public readonly Type delegateType;
        public readonly MethodInfo method;

        private Type[] argTypes;
        public string[] fields;
        private Type[] fieldTypes;

        public SyncDelegate(int syncId, Type delegateType, string delegateMethod, string[] fields) : base(syncId)
        {
            this.delegateType = delegateType;
            method = AccessTools.Method(delegateType, delegateMethod);

            argTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

            this.fields = fields;
            if (fields == null)
            {
                List<string> fieldList = new List<string>();
                Sync.AllDelegateFieldsRecursive(delegateType, path => { fieldList.Add(path); return false; });
                this.fields = fieldList.ToArray();
            }
            else
            {
                for (int i = 0; i < this.fields.Length; i++)
                {
                    this.fields[i] = MpReflection.AppendType(this.fields[i], delegateType);
                }
            }

            fieldTypes = this.fields.Select(path => MpReflection.PropertyOrFieldType(path)).ToArray();
        }

        public void DoSync(object delegateInstance, int mapId, params object[] args)
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(syncId);

            Sync.WriteContext(writer, mapId);

            foreach (string field in fields)
                Sync.WriteSyncObject(writer, delegateInstance.GetPropertyOrField(field));

            Sync.WriteSyncObjects(writer, args, argTypes);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());
        }

        public override void Read(ByteReader data)
        {
            object target = Activator.CreateInstance(delegateType);
            for (int i = 0; i < fields.Length; i++)
                MpReflection.SetPropertyOrField(target, fields[i], Sync.ReadSyncObject(data, fieldTypes[i]));

            object[] parameters = Sync.ReadSyncObjects(data, argTypes);

            MpLog.Log("Invoked delegate method " + method + " " + delegateType);
            method.Invoke(target, parameters);
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
        public static Dictionary<MethodBase, SyncDelegate> delegates = new Dictionary<MethodBase, SyncDelegate>();

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
                object newValue = data.target.GetPropertyOrField(data.handler.memberPath);

                if (!Equals(newValue, data.value))
                {
                    MpReflection.SetPropertyOrField(data.target, data.handler.memberPath, data.value);
                    data.handler.DoSync(data.target, newValue);
                }
            }

            currentMethod.Clear();
            syncing = __state = false;
        }

        public static SyncMethod Method(Type targetType, string instancePath, string methodName)
        {
            SyncMethod handler = new SyncMethod(handlers.Count, targetType, instancePath, methodName);
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

        public static void Delegate(object instance, object mapProvider, params object[] args)
        {
            MethodBase caller = new StackTrace().GetFrame(1).GetMethod();
            SyncDelegate handler = delegates[caller];
            int mapId = ScheduledCommand.GLOBAL;

            if (mapProvider == MapProviderMode.ANY_PAWN)
            {
                foreach (string path in handler.fields)
                {
                    object obj = instance.GetPropertyOrField(path);
                    if (obj is Pawn pawn)
                    {
                        mapId = pawn.Map.uniqueID;
                        break;
                    }
                }
            }
            else if (mapProvider is Pawn pawn)
            {
                mapId = pawn.Map.uniqueID;
            }

            args = args ?? new object[0];
            handler.DoSync(instance, mapId, args);
        }

        public static bool AllDelegateFieldsRecursive(Type type, Func<string, bool> getter, string path = "")
        {
            if (path.NullOrEmpty())
                path = type.ToString();

            foreach (FieldInfo field in AccessTools.GetDeclaredFields(type))
            {
                string curPath = path + "/" + field.Name;

                if (getter(curPath))
                    return true;

                if (Attribute.GetCustomAttribute(field.FieldType, typeof(CompilerGeneratedAttribute)) == null)
                    continue;

                if (AllDelegateFieldsRecursive(field.FieldType, getter, curPath))
                    return true;
            }

            return false;
        }

        public static void RegisterSyncDelegates(Type inType)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(inType))
            {
                if (!method.TryGetAttribute(out SyncDelegateAttribute syncAttr))
                    continue;

                if (!method.TryGetAttribute(out MpPatch patchAttr))
                    continue;

                Type type = patchAttr.type ?? MpReflection.GetTypeByName(patchAttr.typeName);
                SyncDelegate handler = new SyncDelegate(handlers.Count, type, patchAttr.method, syncAttr.fields);
                delegates[method] = handler;
                handlers.Add(handler);
            }
        }

        public static bool CanSerialize(Type type)
        {
            return type.GetConstructors(AccessTools.all).Any(c => c.GetParameters().Length == 0);
        }

        public static void RegisterFieldPatches(Type type)
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

            if (data.context is Map)
            {
                IntVec3 mouseCell = ReadSyncObject<IntVec3>(data);
                MouseCellPatch.result = mouseCell;
            }

            bool shouldQueue = data.ReadBool();
            KeyIsDownPatch.result = shouldQueue;
            KeyIsDownPatch.forKey = KeyBindingDefOf.QueueOrder;

            handler.Read(data);

            MouseCellPatch.result = null;
            KeyIsDownPatch.result = null;
            KeyIsDownPatch.forKey = null;
        }

        public static void WriteContext(ByteWriter data, int mapId)
        {
            if (mapId >= 0)
                WriteSyncObject(data, UI.MouseCell());

            data.WriteBool(KeyBindingDefOf.QueueOrder.IsDownEvent);
        }

        public static int GetMapId(object obj)
        {
            if (obj is Pawn pawn)
                return pawn.Map.uniqueID;

            return ScheduledCommand.GLOBAL;
        }

        public static object ReadTarget(ByteReader data, Type targetType)
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
            { data => new IntVec3(data.ReadInt32(), data.ReadInt32(), data.ReadInt32()) },
            { data => ReadSyncObject<Pawn>(data).mindState.priorityWork },
            { data => ReadSyncObject<Pawn>(data).playerSettings },
            { data =>
            {
                Thing thing = ReadSyncObject<Thing>(data);
                if (thing != null)
                    return new LocalTargetInfo(thing);
                else
                    return new LocalTargetInfo(ReadSyncObject<IntVec3>(data));
            }
            }
        };

        static WriterDictionary writers = new WriterDictionary
        {
            { (ByteWriter data, int o) => data.WriteInt32(o) },
            { (ByteWriter data, bool o) => data.WriteBool(o) },
            { (ByteWriter data, string o) => data.Write(o) },
            { (ByteWriter data, PriorityWork work) => WriteSyncObject(data, work.GetPropertyOrField("pawn")) },
            { (ByteWriter data, Pawn_PlayerSettings settings) => WriteSyncObject(data, settings.GetPropertyOrField("pawn")) },
            {
                (ByteWriter data, IntVec3 vec) =>
                {
                    data.WriteInt32(vec.x);
                    data.WriteInt32(vec.y);
                    data.WriteInt32(vec.z);
                }
            },
            {
                (ByteWriter data, LocalTargetInfo info) =>
                {
                    WriteSyncObject(data, info.Thing);
                    if (!info.HasThing)
                        WriteSyncObject(data, info.Cell);
                }
            }
        };

        public static T ReadSyncObject<T>(ByteReader data)
        {
            return (T)ReadSyncObject(data, typeof(T));
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
                    return null;

                Map map = data.context as Map;
                return map.areaManager.AllAreas.FirstOrDefault(a => a.ID == areaId);
            }
            else if (typeof(Def).IsAssignableFrom(type))
            {
                ushort shortHash = data.ReadUInt16();
                if (shortHash == 0)
                    return null;

                Type dbType = typeof(DefDatabase<>).MakeGenericType(type);
                return AccessTools.Method(dbType, "GetByShortHash").Invoke(null, new object[] { shortHash });
            }
            else if (typeof(Thing).IsAssignableFrom(type))
            {
                int thingId = data.ReadInt32();
                if (thingId == -1)
                    return null;

                ThingDef def = ReadSyncObject<ThingDef>(data);
                Map map = data.context as Map;

                return map.listerThings.ThingsOfDef(def).FirstOrDefault(t => t.thingIDNumber == thingId);
            }
            else if (readers.TryGetValue(type, out Func<ByteReader, object> reader))
            {
                return reader(data);
            }
            else
            {
                throw new SerializationException("No reader for type " + type);
            }
        }

        public static object[] ReadSyncObjects(ByteReader data, Type[] spec)
        {
            return spec.Select(type => ReadSyncObject(data, type)).ToArray();
        }

        public static void WriteSyncObject(ByteWriter data, object obj)
        {
            WriteSyncObject(data, obj, obj.GetType());
        }

        public static void WriteSyncObject<T>(ByteWriter data, T obj)
        {
            WriteSyncObject(data, obj, typeof(T));
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
            else if (typeof(Thing).IsAssignableFrom(type))
            {
                Thing thing = (Thing)obj;
                if (thing == null)
                {
                    data.WriteInt32(-1);
                    return;
                }

                data.WriteInt32(thing.thingIDNumber);
                WriteSyncObject(data, thing.def);
            }
            else if (writers.TryGetValue(type, out Action<ByteWriter, object> writer))
            {
                writer(data, obj);
            }
            else
            {
                throw new SerializationException("No writer for type " + type);
            }
        }

        public static void WriteSyncObjects(ByteWriter data, object[] objs, Type[] spec)
        {
            for (int i = 0; i < spec.Length; i++)
                WriteSyncObject(data, objs[i], spec[i]);
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

    public class SerializationException : Exception
    {
        public SerializationException(string msg) : base(msg)
        {
        }
    }

}
