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
        public static SyncField SyncThingFilterHitPoints = Sync.Field(typeof(ThingFilterWrapper), "filter", "AllowedHitPointsPercents");
        public static SyncField SyncThingFilterQuality = Sync.Field(typeof(ThingFilterWrapper), "filter", "AllowedQualityLevels");

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
        static void DoAreaSelector_Prefix(Pawn p)
        {
            SyncAreaRestriction.Watch(p);
        }

        [MpPatch(typeof(PawnColumnWorker_AllowedArea), "HeaderClicked")]
        static void AllowedArea_HeaderClicked_Prefix(PawnTable table)
        {
            foreach (Pawn pawn in table.PawnsListForReading)
                SyncAreaRestriction.Watch(pawn);
        }

        [MpPatch("RimWorld.InspectPaneFiller+<DrawAreaAllowed>c__AnonStorey0", "<>m__0")]
        static void DrawAreaAllowed_Inner(object __instance)
        {
            SyncAreaRestriction.Watch(__instance, "pawn");
        }

        [MpPatch(typeof(HealthCardUtility), "DrawOverviewTab")]
        static void HealthCardUtility1(Pawn pawn)
        {
            SyncMedCare.Watch(pawn);
            SyncSelfTend.Watch(pawn);
        }

        [MpPatch(typeof(ITab_Pawn_Visitor), "FillTab")]
        static void ITab_Pawn_Visitor(ITab __instance)
        {
            SyncMedCare.Watch(__instance, "SelPawn");
            SyncGetsFood.Watch(__instance, "SelPawn");
            SyncInteractionMode.Watch(__instance, "SelPawn");
        }

        [MpPatch(typeof(HostilityResponseModeUtility), "DrawResponseButton")]
        static void DrawResponseButton(Pawn pawn)
        {
            SyncHostilityResponse.Watch(pawn);
        }

        [MpPatch(typeof(PawnColumnWorker_FollowFieldwork), "SetValue")]
        static void FollowFieldwork(Pawn pawn)
        {
            SyncFollowFieldwork.Watch(pawn);
        }

        [MpPatch(typeof(PawnColumnWorker_FollowDrafted), "SetValue")]
        static void FollowDrafted(Pawn pawn)
        {
            SyncFollowDrafted.Watch(pawn);
        }

        [MpPatch(typeof(TrainableUtility), "<OpenMasterSelectMenu>c__AnonStorey0", "<>m__0")]
        static void OpenMasterSelectMenu_Inner1(object __instance)
        {
            SyncMaster.Watch(__instance, "p");
        }

        [MpPatch(typeof(TrainableUtility), "<OpenMasterSelectMenu>c__AnonStorey1", "<>m__0")]
        static void OpenMasterSelectMenu_Inner2(object __instance)
        {
            SyncMaster.Watch(__instance, "<>f__ref$0/p");
        }

        [MpPatch(typeof(Dialog_MedicalDefaults), "DoWindowContents")]
        static void MedicalDefaults()
        {
            SyncDefaultCare.Watch();
        }

        [MpPatch(typeof(Widgets), "CheckboxLabeled")]
        static void CheckboxLabeled()
        {
            if (MethodMarkers.manualPriorities)
                SyncUseWorkPriorities.Watch();
        }

        [MpPatch(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
        static void PlaySettingsControls()
        {
            SyncAutoHomeArea.Watch();
        }

        [MpPatch(typeof(ThingFilterUI), "DrawHitPointsFilterConfig")]
        static void ThingFilterHitPoints()
        {
            ThingFilterWrapper? owner = GetThingFilterWrapper();
            if (!owner.HasValue || !owner.Value.filter.allowedHitPointsConfigurable) return;
            SyncThingFilterHitPoints.Watch(owner.Value);
        }

        [MpPatch(typeof(ThingFilterUI), "DrawQualityFilterConfig")]
        static void ThingFilterQuality()
        {
            ThingFilterWrapper? owner = GetThingFilterWrapper();
            if (!owner.HasValue || !owner.Value.filter.allowedQualitiesConfigurable) return;
            SyncThingFilterQuality.Watch(owner.Value);
        }

        private static ThingFilterWrapper? GetThingFilterWrapper()
        {
            if (MethodMarkers.tabStorage != null)
            {
                StorageSettings settings = MethodMarkers.tabStorage.GetStoreSettings();
                return new ThingFilterWrapper(settings, settings.filter);
            }

            if (MethodMarkers.billConfig != null)
            {
                Bill bill = MethodMarkers.billConfig;
                return new ThingFilterWrapper(bill, bill.ingredientFilter);
            }

            return null;
        }
    }

    public static class SyncPatches
    {
        public static SyncMethod SyncSetAssignment = Sync.Method(typeof(Pawn), "timetable", "SetAssignment");
        public static SyncMethod SyncSetWorkPriority = Sync.Method(typeof(Pawn), "workSettings", "SetPriority");
        public static SyncMethod SyncSetDrafted = Sync.Method(typeof(Pawn), "drafter", "set_Drafted");
        public static SyncMethod SyncSetFireAtWill = Sync.Method(typeof(Pawn), "drafter", "set_FireAtWill");
        public static SyncMethod SyncStartJob = Sync.Method(typeof(Pawn), "jobs", "StartJob");
        public static SyncMethod SyncTryTakeOrderedJob = Sync.Method(typeof(Pawn), "jobs", "TryTakeOrderedJob");
        public static SyncMethod SyncTryTakeOrderedJobPrioritizedWork = Sync.Method(typeof(Pawn), "jobs", "TryTakeOrderedJobPrioritizedWork");
        public static SyncMethod SyncSetStoragePriority = Sync.Method(typeof(IStoreSettingsParent), "GetStoreSettings", "set_Priority");

        public static SyncField SyncTimetable = Sync.Field(typeof(Pawn), "timetable", "times");

        [MpPatch(typeof(Pawn_TimetableTracker), "SetAssignment")]
        static bool SetTimetableAssignment(Pawn_TimetableTracker __instance, int hour, TimeAssignmentDef ta)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncSetAssignment.DoSync(__instance.GetPropertyOrField("pawn"), hour, ta);
            return false;
        }

        [MpPatch(typeof(PawnColumnWorker_CopyPasteTimetable), "PasteTo")]
        static bool CopyPasteTimetable(Pawn p)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncTimetable.DoSync(p, MpReflection.GetPropertyOrField("PawnColumnWorker_CopyPasteTimetable.clipboard"));
            return false;
        }

        [MpPatch(typeof(Pawn_WorkSettings), "SetPriority")]
        static bool SetWorkPriority(Pawn_WorkSettings __instance, WorkTypeDef w, int priority)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncSetWorkPriority.DoSync(__instance.GetPropertyOrField("pawn"), w, priority);
            return false;
        }

        [MpPatch(typeof(Pawn_DraftController), "set_Drafted")]
        static bool SetDrafted(Pawn_DraftController __instance, bool value)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncSetDrafted.DoSync(__instance.pawn, value);
            return false;
        }

        [MpPatch(typeof(Pawn_DraftController), "set_FireAtWill")]
        static bool SetFireAtWill(Pawn_DraftController __instance, bool value)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncSetFireAtWill.DoSync(__instance.pawn, value);
            return false;
        }

        [MpPatch(typeof(Pawn_JobTracker), "StartJob")]
        static bool StartJob(Pawn_JobTracker __instance, Job newJob, JobCondition lastJobEndCondition, ThinkNode jobGiver, bool resumeCurJobAfterwards, bool cancelBusyStances, ThinkTreeDef thinkTree, JobTag? tag, bool fromQueue)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncSetFireAtWill.DoSync(__instance.GetPropertyOrField("pawn"), newJob, lastJobEndCondition, jobGiver, resumeCurJobAfterwards, cancelBusyStances, thinkTree, tag, fromQueue);
            return false;
        }

        [MpPatch(typeof(StorageSettings), "set_Priority")]
        static bool StorageSetPriority(StorageSettings __instance, StoragePriority value)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncSetStoragePriority.DoSync(__instance.owner, value);
            return false;
        }

        [MpPatch(typeof(Pawn_JobTracker), "TryTakeOrderedJob")]
        static bool TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, JobTag tag)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncTryTakeOrderedJob.DoSync(__instance.GetPropertyOrField("pawn"), job, tag);
            return false;
        }

        [MpPatch(typeof(Pawn_JobTracker), "TryTakeOrderedJobPrioritizedWork")]
        static bool TryTakeOrderedJobPrioritizedWork(Pawn_JobTracker __instance, Job job, WorkGiver giver, IntVec3 cell)
        {
            if (!Multiplayer.ShouldSync) return true;
            SyncTryTakeOrderedJobPrioritizedWork.DoSync(__instance.GetPropertyOrField("pawn"), job, giver, cell);
            return false;
        }
    }

    public struct ThingFilterWrapper
    {
        public readonly object owner;
        public readonly ThingFilter filter;

        public ThingFilterWrapper(object owner, ThingFilter filter)
        {
            this.owner = owner;
            this.filter = filter;
        }
    }

    public static class SyncDelegates
    {
        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<GotoLocationOption>c__AnonStorey18", "<>m__0")]
        static bool FloatMenuGoto(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_FIELD);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<AddDraftedOrders>c__AnonStorey3", "<>m__0")]
        static bool FloatMenuArrest(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_FIELD);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__0")]
        static bool FloatMenuRescue(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_FIELD);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__1")]
        static bool FloatMenuCapture(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_FIELD);
            return false;
        }

        [SyncDelegate]
        [MpPatch(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey9", "<>m__0")]
        static bool FloatMenuCarryToCryptosleepCasket(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MapProviderMode.ANY_FIELD);
            return false;
        }

        [SyncDelegate("$this")]
        [MpPatch(typeof(Pawn_PlayerSettings), "<GetGizmos>c__Iterator0", "<>m__2")]
        static bool GizmoReleaseAnimals(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, __instance.GetPropertyOrField("$this/pawn"));
            return false;
        }

        [SyncDelegate("$this")]
        [MpPatch(typeof(PriorityWork), "<GetGizmos>c__Iterator0", "<>m__0")]
        static bool GizmoClearPrioritizedWork(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, __instance.GetPropertyOrField("$this/pawn"));
            return false;
        }

        /*[SyncDelegate("lord")]
        [MpPatch(typeof(Pawn_MindState), "<GetGizmos>c__Iterator0+<GetGizmos>c__AnonStorey2", "<>m__0")]
        static bool GizmoCancelFormingCaravan(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MpReflection.GetPropertyOrField(__instance, ""));
            return false;
        }*/
    }

    public static class MapProviderMode
    {
        public static readonly object ANY_FIELD = new object();
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
        public static IStoreSettingsParent tabStorage;
        public static Bill billConfig;
        public static Outfit dialogOutfit;

        [MpPatch(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Prefix() => manualPriorities = true;

        [MpPatch(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        [MpPostfix]
        static void ManualPriorities_Postfix() => manualPriorities = false;

        [MpPatch(typeof(ITab_Storage), "FillTab")]
        static void TabStorageFillTab_Prefix(ITab_Storage __instance) => tabStorage = (IStoreSettingsParent)__instance.GetPropertyOrField("SelStoreSettingsParent");

        [MpPatch(typeof(ITab_Storage), "FillTab")]
        [MpPostfix]
        static void TabStorageFillTab_Postfix() => tabStorage = null;

        [MpPatch(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Prefix(Dialog_BillConfig __instance) => billConfig = (Bill)__instance.GetPropertyOrField("bill");

        [MpPatch(typeof(Dialog_BillConfig), "DoWindowContents")]
        [MpPostfix]
        static void BillConfig_Postfix() => billConfig = null;

        [MpPatch(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Prefix(Dialog_ManageOutfits __instance) => dialogOutfit = (Outfit)__instance.GetPropertyOrField("SelectedOutfit");

        [MpPatch(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        [MpPostfix]
        static void ManageOutfit_Postfix() => billConfig = null;
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

    public interface Test1
    {
        void a();
    }

    public abstract class test2 : Test1
    {
        public abstract void a();
    }

    public class SyncField : SyncHandler
    {
        public readonly Type targetType;
        public readonly string memberPath;
        public readonly Type fieldType;
        public readonly Func<object, object> instanceFunc;

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
            Map map = Sync.GetMap(target);
            int mapId = map != null ? map.uniqueID : ScheduledCommand.GLOBAL;
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(syncId);

            Sync.WriteContext(writer, mapId);
            Sync.WriteSyncObject(writer, target, targetType);
            Sync.WriteSyncObject(writer, value, fieldType);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());
        }

        public override void Read(ByteReader data)
        {
            object target = Sync.ReadSyncObject(data, targetType);
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
            int mapId = Sync.GetMap(target).uniqueID;
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(syncId);

            Sync.WriteContext(writer, mapId);
            Sync.WriteSyncObject(writer, target, targetType);
            Sync.WriteSyncObjects(writer, args, argTypes);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());
        }

        public override void Read(ByteReader data)
        {
            object target = Sync.ReadSyncObject(data, targetType);
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

    public enum StoreSettingsParent
    {
        THING,
        THING_COMP,
        ZONE
    }

    public enum ThingFilterOwner
    {
        STORAGE,
        BILL,
        OUTFIT
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

            if (mapProvider == MapProviderMode.ANY_FIELD)
            {
                foreach (string path in handler.fields)
                {
                    object obj = instance.GetPropertyOrField(path);
                    if (GetMap(obj) != null)
                    {
                        mapId = GetMap(obj).uniqueID;
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
                IntVec3 mouseCell = ReadSync<IntVec3>(data);
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
                WriteSync(data, UI.MouseCell());

            data.WriteBool(KeyBindingDefOf.QueueOrder.IsDownEvent);
        }

        public static Map GetMap(object obj)
        {
            if (obj is Thing thing && thing.Spawned)
            {
                return thing.Map;
            }
            else if (obj is ThingComp comp)
            {
                return GetMap(comp.parent);
            }
            else if (obj is Zone zone)
            {
                return zone.Map;
            }
            else if (obj is Bill bill)
            {
                return bill.Map;
            }
            else if (obj is ThingFilterWrapper filter)
            {
                return GetMap(filter.owner);
            }

            return null;
        }

        static ReaderDictionary readers = new ReaderDictionary
        {
            { data => data.ReadInt32() },
            { data => data.ReadBool() },
            { data => data.ReadString() },
            { data => data.ReadLong() },
            { data => new IntVec3(data.ReadInt32(), data.ReadInt32(), data.ReadInt32()) },
            { data => ReadSync<Pawn>(data).mindState.priorityWork },
            { data => ReadSync<Pawn>(data).playerSettings },
            { data => ScribeUtil.ReadExposable<Job>(data.ReadPrefixedBytes()) },
            { data => new FloatRange(data.ReadFloat(), data.ReadFloat()) },
            { data => new IntRange(data.ReadInt32(), data.ReadInt32()) },
            { data => new QualityRange(ReadSync<QualityCategory>(data), ReadSync<QualityCategory>(data)) },
            { data =>
            {
                Thing thing = ReadSync<Thing>(data);
                if (thing != null)
                    return new LocalTargetInfo(thing);
                else
                    return new LocalTargetInfo(ReadSync<IntVec3>(data));
            }
            }
        };

        static WriterDictionary writers = new WriterDictionary
        {
            { (ByteWriter data, int i) => data.WriteInt32(i) },
            { (ByteWriter data, bool b) => data.WriteBool(b) },
            { (ByteWriter data, string s) => data.WriteString(s) },
            { (ByteWriter data, long l) => data.WriteLong(l) },
            { (ByteWriter data, PriorityWork work) => WriteSyncObject(data, work.GetPropertyOrField("pawn")) },
            { (ByteWriter data, Pawn_PlayerSettings settings) => WriteSyncObject(data, settings.GetPropertyOrField("pawn")) },
            { (ByteWriter data, Job job) => data.WritePrefixedBytes(ScribeUtil.WriteExposable(job)) },
            { (ByteWriter data, FloatRange range) => { data.WriteFloat(range.min); data.WriteFloat(range.max); }},
            { (ByteWriter data, IntRange range) => { data.WriteInt32(range.min); data.WriteInt32(range.max); }},
            { (ByteWriter data, QualityRange range) => { WriteSync(data, range.min); WriteSync(data, range.max); }},
            { (ByteWriter data, IntVec3 vec) => { data.WriteInt32(vec.x); data.WriteInt32(vec.y); data.WriteInt32(vec.z); }},
            {
                (ByteWriter data, LocalTargetInfo info) =>
                {
                    WriteSync(data, info.Thing);
                    if (!info.HasThing)
                        WriteSync(data, info.Cell);
                }
            }
        };

        public static T ReadSync<T>(ByteReader data)
        {
            return (T)ReadSyncObject(data, typeof(T));
        }

        public static object ReadSyncObject(ByteReader data, Type type)
        {
            Map map = data.context as Map;

            if (type.IsEnum)
            {
                return Enum.ToObject(type, data.ReadInt32());
            }
            else if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    Type listType = type.GetGenericArguments()[0];
                    int size = data.ReadInt32();
                    IList list = Activator.CreateInstance(type, size) as IList;
                    for (int j = 0; j < size; j++)
                        list.Add(ReadSyncObject(data, listType));
                    return list;
                }
                else if (type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    bool hasValue = data.ReadBool();
                    if (!hasValue)
                        return Activator.CreateInstance(type);

                    Type nullableType = type.GetGenericArguments()[0];
                    return Activator.CreateInstance(type, ReadSyncObject(data, nullableType));
                }
            }
            else if (typeof(ThinkNode).IsAssignableFrom(type))
            {
                // todo temporary
                return null;
            }
            else if (typeof(Area).IsAssignableFrom(type))
            {
                int areaId = data.ReadInt32();
                if (areaId == -1)
                    return null;

                return map.areaManager.AllAreas.FirstOrDefault(a => a.ID == areaId);
            }
            else if (typeof(Zone).IsAssignableFrom(type))
            {
                string name = data.ReadString();
                Log.Message("Reading zone " + name + " " + map);
                if (name.NullOrEmpty())
                    return null;

                return map.zoneManager.AllZones.FirstOrDefault(zone => zone.label == name);
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

                ThingDef def = ReadSync<ThingDef>(data);
                return map.listerThings.ThingsOfDef(def).FirstOrDefault(t => t.thingIDNumber == thingId);
            }
            else if (typeof(ThingComp).IsAssignableFrom(type))
            {
                string compType = data.ReadString();
                if (compType.NullOrEmpty())
                    return null;

                ThingWithComps parent = ReadSync<Thing>(data) as ThingWithComps;
                if (parent == null)
                    return null;

                return parent.AllComps.FirstOrDefault(comp => comp.props.compClass.FullName == compType);
            }
            else if (typeof(WorkGiver).IsAssignableFrom(type))
            {
                WorkGiverDef def = ReadSync<WorkGiverDef>(data);
                return def?.Worker;
            }
            else if (typeof(BillStack) == type)
            {
                Thing thing = ReadSync<Thing>(data);
                if (thing is IBillGiver billGiver)
                    return billGiver.BillStack;
                return null;
            }
            else if (typeof(Bill).IsAssignableFrom(type))
            {
                BillStack billStack = ReadSync<BillStack>(data);
                if (billStack == null)
                    return null;

                int id = data.ReadInt32();
                return billStack.Bills.FirstOrDefault(bill => (int)bill.GetPropertyOrField("loadId") == id);
            }
            else if (typeof(ThingFilterWrapper) == type)
            {
                ThingFilterOwner ownerType = ReadSync<ThingFilterOwner>(data);

                if (ownerType == ThingFilterOwner.STORAGE)
                {
                    IStoreSettingsParent storage = ReadSync<IStoreSettingsParent>(data);
                    return new ThingFilterWrapper(storage, storage.GetStoreSettings().filter);
                }
                else if (ownerType == ThingFilterOwner.BILL)
                {
                    Bill bill = ReadSync<Bill>(data);
                    return new ThingFilterWrapper(bill, bill.ingredientFilter);
                }
            }
            else if (typeof(IStoreSettingsParent) == type)
            {
                StoreSettingsParent parentType = ReadSync<StoreSettingsParent>(data);

                if (parentType == StoreSettingsParent.THING)
                    return ReadSync<Thing>(data) as IStoreSettingsParent;
                else if (parentType == StoreSettingsParent.THING_COMP)
                    return ReadSync<ThingComp>(data) as IStoreSettingsParent;
                else if (parentType == StoreSettingsParent.ZONE)
                    return ReadSync<Zone>(data) as IStoreSettingsParent;
                else
                    return null;
            }
            else if (readers.TryGetValue(type, out Func<ByteReader, object> reader))
            {
                return reader(data);
            }

            throw new SerializationException("No reader for type " + type);
        }

        public static object[] ReadSyncObjects(ByteReader data, Type[] spec)
        {
            return spec.Select(type => ReadSyncObject(data, type)).ToArray();
        }

        public static void WriteSync<T>(ByteWriter data, T obj)
        {
            WriteSyncObject(data, obj, typeof(T));
        }

        public static void WriteSyncObject(ByteWriter data, object obj)
        {
            WriteSyncObject(data, obj, obj.GetType());
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
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                Type nullableType = type.GetGenericArguments()[0];
                bool hasValue = (bool)obj.GetPropertyOrField("HasValue");

                data.WriteBool(hasValue);
                if (hasValue)
                    WriteSyncObject(data, obj.GetPropertyOrField("Value"));
            }
            else if (typeof(ThinkNode).IsAssignableFrom(type))
            {
                // todo temporary
            }
            else if (typeof(Area).IsAssignableFrom(type))
            {
                data.WriteInt32(obj is Area area ? area.ID : -1);
            }
            else if (typeof(Zone).IsAssignableFrom(type))
            {
                data.WriteString(obj is Zone zone ? zone.label : "");
            }
            else if (typeof(Def).IsAssignableFrom(type))
            {
                data.WriteUInt16(obj is Def def ? def.shortHash : (ushort)0);
            }
            else if (typeof(ThingComp).IsAssignableFrom(type))
            {
                if (obj is ThingComp comp)
                {
                    data.WriteString(comp.props.compClass.FullName);
                    WriteSync<Thing>(data, comp.parent);
                }
                else
                {
                    data.WriteString("");
                }
            }
            else if (typeof(WorkGiver).IsAssignableFrom(type))
            {
                WorkGiver workGiver = obj as WorkGiver;
                if (workGiver == null)
                    return;

                WriteSync(data, workGiver);
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
                WriteSync(data, thing.def);
            }
            else if (typeof(BillStack) == type)
            {
                Thing billGiver = (obj as BillStack)?.billGiver as Thing;
                WriteSync(data, billGiver);
            }
            else if (typeof(Bill) == type)
            {
                Bill bill = obj as Bill;
                WriteSync(data, bill.billStack);
                data.WriteInt32((int)bill.GetPropertyOrField("loadId"));
            }
            else if (typeof(ThingFilterWrapper) == type)
            {
                ThingFilterWrapper wrapper = (ThingFilterWrapper)obj;

                if (wrapper.owner is IStoreSettingsParent storage)
                {
                    WriteSync(data, ThingFilterOwner.STORAGE);
                    WriteSyncObject(data, wrapper.owner, typeof(IStoreSettingsParent));
                }
                else if (wrapper.owner is Bill bill)
                {
                    WriteSync(data, ThingFilterOwner.BILL);
                    WriteSyncObject(data, wrapper.owner, typeof(Bill));
                }
                else
                {
                    throw new SerializationException("Unknown thing filter owner type: " + wrapper.owner?.GetType());
                }
            }
            else if (typeof(IStoreSettingsParent) == type)
            {
                IStoreSettingsParent owner = obj as IStoreSettingsParent;

                if (owner is Thing)
                {
                    WriteSync(data, StoreSettingsParent.THING);
                    WriteSyncObject(data, owner, typeof(Thing));
                }
                else if (owner is ThingComp)
                {
                    WriteSync(data, StoreSettingsParent.THING_COMP);
                    WriteSyncObject(data, owner, typeof(ThingComp));
                }
                else if (owner is Zone)
                {
                    WriteSync(data, StoreSettingsParent.ZONE);
                    WriteSyncObject(data, owner, typeof(Zone));
                }
                else
                {
                    throw new SerializationException("Unknown storage settings parent type: " + owner?.GetType());
                }
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
