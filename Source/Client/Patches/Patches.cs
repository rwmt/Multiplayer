using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Multiplayer.Client.Patches;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(GameEnder))]
    [HarmonyPatch(nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class GameEnderPatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    static class JobTrackerStartFixFrames
    {
        static FieldInfo FrameCountField = AccessTools.Field(typeof(RealTime), nameof(RealTime.frameCount));
        static MethodInfo FrameCountReplacementMethod = AccessTools.Method(typeof(JobTrackerStartFixFrames), nameof(FrameCountReplacement));

        // Transpilers should be a last resort but there's no better way to patch this
        // and handling this case properly might prevent some crashes and help with debugging
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == FrameCountField)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, FrameCountReplacementMethod);
                }
            }
        }

        static int FrameCountReplacement(int frameCount, Pawn_JobTracker tracker)
        {
            return tracker.pawn.Map.AsyncTime()?.eventCount ?? frameCount;
        }
    }

    [HarmonyPatch(typeof(Dialog_BillConfig), MethodType.Constructor)]
    [HarmonyPatch(new[] {typeof(Bill_Production), typeof(IntVec3)})]
    public static class DialogPatch
    {
        static void Postfix(Dialog_BillConfig __instance)
        {
            __instance.absorbInputAroundWindow = false;
        }
    }

    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.WindowsForcePause), MethodType.Getter)]
    public static class WindowsPausePatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
    }

    /*
    [HarmonyPatch(typeof(Thing), nameof(Thing.ExposeData))]
    public static class PawnExposeDataFirst
    {
        public static Container<Map>? state;

        // Postfix so Thing's faction is already loaded
        static void Postfix(Thing __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!(__instance is Pawn)) return;
            if (__instance.Faction == null) return;
            if (Find.FactionManager == null) return;
            if (Find.FactionManager.AllFactionsListForReading.Count == 0) return;

            __instance.Map.PushFaction(__instance.Faction);
            ThingContext.Push(__instance);
            state = __instance.Map;
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    public static class PawnExposeDataLast
    {
        static void Postfix()
        {
            if (PawnExposeDataFirst.state != null)
            {
                ThingContext.Pop();
                PawnExposeDataFirst.state.PopFaction();
                PawnExposeDataFirst.state = null;
            }
        }
    }*/

    // why patch it if it's commented out?
    //[HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    public static class PreDrawPosCalcPatch
    {
        static void Prefix()
        {
            //if (MapAsyncTimeComp.tickingMap != null)
            //    SimpleProfiler.Pause();
        }

        static void Postfix()
        {
            //if (MapAsyncTimeComp.tickingMap != null)
            //    SimpleProfiler.Start();
        }
    }

    public static class ValueSavePatch
    {
        public static bool DoubleSave_Prefix(string label, ref double value)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return true;
            Scribe.saver.WriteElement(label, value.ToString("G17"));
            return false;
        }

        public static bool FloatSave_Prefix(string label, ref float value)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return true;
            Scribe.saver.WriteElement(label, value.ToString("G9"));
            return false;
        }
    }

    //[HarmonyPatch(typeof(Log), nameof(Log.Warning))]
    public static class CrossRefWarningPatch
    {
        private static Regex regex = new Regex(@"^Could not resolve reference to object with loadID ([\w.-]*) of type ([\w.<>+]*)\. Was it compressed away");
        public static bool ignore;

        // The only non-generic entry point during cross reference resolving
        static bool Prefix(string text)
        {
            if (Multiplayer.Client == null || ignore) return true;

            ignore = true;

            GroupCollection groups = regex.Match(text).Groups;
            if (groups.Count == 3)
            {
                string loadId = groups[1].Value;
                string typeName = groups[2].Value;
                // todo
                return false;
            }

            ignore = false;

            return true;
        }
    }

    [HarmonyPatch(typeof(UI), nameof(UI.MouseCell))]
    public static class MouseCellPatch
    {
        public static IntVec3? result;

        static void Postfix(ref IntVec3 __result)
        {
            if (result.HasValue)
                __result = result.Value;
        }
    }

    [HarmonyPatch(typeof(GenWorld), nameof(GenWorld.MouseTile))]
    public static class MouseTilePatch
    {
        public static int? result;

        static void Postfix(ref int __result)
        {
            if (result.HasValue)
                __result = result.Value;
        }
    }

    [HarmonyPatch(typeof(KeyBindingDef), nameof(KeyBindingDef.IsDownEvent), MethodType.Getter)]
    public static class KeyIsDownPatch
    {
        public static bool? shouldQueue;

        static bool Prefix(KeyBindingDef __instance) => !(__instance == KeyBindingDefOf.QueueOrder && shouldQueue.HasValue);

        static void Postfix(KeyBindingDef __instance, ref bool __result)
        {
            if (__instance == KeyBindingDefOf.QueueOrder && shouldQueue.HasValue)
                __result = shouldQueue.Value;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    static class PawnSpawnSetupMarker
    {
        public static bool currentlyRespawningAfterLoad;

        static void Prefix(bool respawningAfterLoad)
        {
            currentlyRespawningAfterLoad = respawningAfterLoad;
        }

        static void Finalizer()
        {
            currentlyRespawningAfterLoad = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.ResetToCurrentPosition))]
    static class PatherResetPatch
    {
        static bool Prefix() => !PawnSpawnSetupMarker.currentlyRespawningAfterLoad;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    static class LoadGameMarker
    {
        public static bool loading;

        static void Prefix() => loading = true;
        static void Finalizer() => loading = false;
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Start))]
    static class RootPlayStartMarker
    {
        public static bool starting;

        static void Prefix() => starting = true;
        static void Finalizer() => starting = false;
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(Action) })]
    static class CancelRootPlayStartLongEvents
    {
        public static bool cancel;

        static bool Prefix()
        {
            if (RootPlayStartMarker.starting && cancel) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(ScreenFader), nameof(ScreenFader.SetColor))]
    static class DisableScreenFade1
    {
        static bool Prefix() => LongEventHandler.eventQueue.All(e => e.eventTextKey == "MpLoading");
    }

    [HarmonyPatch(typeof(ScreenFader), nameof(ScreenFader.StartFade), typeof(Color), typeof(float), typeof(float))]
    static class DisableScreenFade2
    {
        static bool Prefix() => LongEventHandler.eventQueue.All(e => e.eventTextKey == "MpLoading");
    }

    [HarmonyPatch(typeof(ThingGrid), nameof(ThingGrid.Register))]
    static class DontEnlistNonSaveableThings
    {
        static bool Prefix(Thing t) => t.def.isSaveable;
    }

    [HarmonyPatch(typeof(IncidentDef), nameof(IncidentDef.TargetAllowed))]
    static class GameConditionIncidentTargetPatch
    {
        static void Postfix(IncidentDef __instance, IIncidentTarget target, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.workerClass == typeof(IncidentWorker_MakeGameCondition) || __instance.workerClass == typeof(IncidentWorker_Aurora))
                __result = target.IncidentTargetTags().Contains(IncidentTargetTagDefOf.Map_PlayerHome);
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_Aurora), nameof(IncidentWorker_Aurora.AuroraWillEndSoon))]
    static class IncidentWorkerAuroraPatch
    {
        static void Postfix(Map map, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            if (map != Multiplayer.MapContext)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(NamePlayerFactionAndSettlementUtility), nameof(NamePlayerFactionAndSettlementUtility.CanNameAnythingNow))]
    static class NoNamingInMultiplayer
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(DirectXmlSaver), nameof(DirectXmlSaver.XElementFromObject), typeof(object), typeof(Type), typeof(string), typeof(FieldInfo), typeof(bool))]
    static class ExtendDirectXmlSaver
    {
        public static bool extend;

        static bool Prefix(object obj, Type expectedType, string nodeName, FieldInfo owningField, ref XElement __result)
        {
            if (!extend) return true;
            if (obj == null) return true;

            if (obj is Array arr)
            {
                var elementType = arr.GetType().GetElementType();
                var listType = typeof(List<>).MakeGenericType(elementType);
                __result = DirectXmlSaver.XElementFromObject(Activator.CreateInstance(listType, arr), listType, nodeName, owningField);
                return false;
            }

            string content = null;

            if (obj is Type type)
                content = type.FullName;
            else if (obj is MethodBase method)
                content = method.MethodDesc();
            else if (obj is Delegate del)
                content = del.Method.MethodDesc();

            if (content != null)
            {
                __result = new XElement(nodeName, content);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Pause))]
    static class TickManagerPausePatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(WorldRoutePlanner), nameof(WorldRoutePlanner.ShouldStop), MethodType.Getter)]
    static class RoutePlanner_ShouldStop_Patch
    {
        static void Postfix(WorldRoutePlanner __instance, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            // Ignore unpausing
            if (__result && __instance.active && WorldRendererUtility.WorldRenderedNow)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    static class CancelSyncDuringPawnGeneration
    {
        static void Prefix() => Multiplayer.dontSync = true;
        static void Finalizer() => Multiplayer.dontSync = false;
    }

    [HarmonyPatch(typeof(DesignationDragger), nameof(DesignationDragger.UpdateDragCellsIfNeeded))]
    static class CancelUpdateDragCellsIfNeeded
    {
        static bool Prefix() => !Multiplayer.ExecutingCmds;
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
    static class WorkPrioritySameValue
    {
        [HarmonyPriority(MpPriority.MpFirst + 1)]
        static bool Prefix(Pawn_WorkSettings __instance, WorkTypeDef w, int priority) => __instance.GetPriority(w) != priority;
    }

    [HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.AreaRestrictionInPawnCurrentMap), MethodType.Setter)]
    static class AreaRestrictionSameValue
    {
        [HarmonyPriority(MpPriority.MpFirst + 1)]
        static bool Prefix(Pawn_PlayerSettings __instance, Area value) => __instance.AreaRestrictionInPawnCurrentMap != value;
    }

    [HarmonyPatch]
    static class PatchQuestChoices
    {
        // It's the only method with a Choice
        static MethodBase TargetMethod()
        {
            return AccessTools.FirstMethod(
                AccessTools.FirstInner(typeof(MainTabWindow_Quests),
                    t => t.GetFields().Any(f => f.Name == "localChoice")
                ), m => !m.IsConstructor);
        }

        static bool Prefix(QuestPart_Choice.Choice ___localChoice)
        {
            if (Multiplayer.Client == null) return true;

            foreach(var part in Find.QuestManager.QuestsListForReading.SelectMany(q => q.parts).OfType<QuestPart_Choice>()) {
                int index = part.choices.IndexOf(___localChoice);

                if (index >= 0) {
                    Choose(part, index);
                    return false;
                }
            }

            // Should not happen!
            Log.Error("Multiplayer :: Choice without QuestPart_Choice... you're about to desync.");
            return true;
        }

        // Registered in SyncMethods.cs
        internal static void Choose(QuestPart_Choice part, int index)
        {
            part.Choose(part.choices[index]);
        }
    }

    [HarmonyPatch(typeof(DiaOption), nameof(DiaOption.Activate))]
    static class NodeTreeDialogSync
    {
        static bool Prefix(DiaOption __instance)
        {
            if (Multiplayer.session == null || !SyncUtil.isDialogNodeTreeOpen || !(__instance.dialog is Dialog_NodeTree dialog))
            {
                SyncUtil.isDialogNodeTreeOpen = false;
                return true;
            }

            // Get the current node, find the index of the option on it, and call a (synced) method
            var currentNode = dialog.curNode;
            int index = currentNode.options.FindIndex(x => x == __instance);
            if (index >= 0)
                SyncDialogOptionByIndex(index);

            return false;
        }

        [SyncMethod]
        internal static void SyncDialogOptionByIndex(int position)
        {

            // Make sure we have the correct dialog and data
            if (position >= 0)
            {
                var dialog = Find.WindowStack.WindowOfType<Dialog_NodeTree>();

                if (dialog != null && position < dialog.curNode.options.Count)
                {
                    SyncUtil.isDialogNodeTreeOpen = false; // Prevents infinite loop, otherwise PreSyncDialog would call this method over and over again
                    var option = dialog.curNode.options[position]; // Get the correct DiaOption
                    option.Activate(); // Call the Activate method to actually "press" the button

                    if (!option.resolveTree) SyncUtil.isDialogNodeTreeOpen = true; // In case dialog is still open, we mark it as such

                    // Try opening the trading menu if the picked option was supposed to do so (caravan meeting, trading option)
                    if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any(t => t.trader is Caravan))
                        Find.WindowStack.Add(new TradingWindow());
                }
                else SyncUtil.isDialogNodeTreeOpen = false;
            }
            else SyncUtil.isDialogNodeTreeOpen = false;
        }
    }

    [HarmonyPatch(typeof(Dialog_NodeTree), nameof(Dialog_NodeTree.PostClose))]
    static class NodeTreeDialogMarkClosed
    {
        // Set the dialog as closed in here as well just in case
        static void Prefix() => SyncUtil.isDialogNodeTreeOpen = false;
    }

    [HarmonyPatch]
    static class SetGodModePatch
    {
        static IEnumerable<MethodInfo> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DrawButtons));
            yield return AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DevToolStarterOnGUI));
            yield return AccessTools.PropertySetter(typeof(Prefs), nameof(Prefs.DevMode));
        }

        static void Prefix(ref bool __state)
        {
            __state = DebugSettings.godMode;
        }

        static void Postfix(bool __state)
        {
            if (Multiplayer.Client != null && __state != DebugSettings.godMode)
                Multiplayer.GameComp.SetGodMode(Multiplayer.session.playerId, DebugSettings.godMode);
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    static class AllowCurrentMapNullWhenLoading
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var list = insts.ToList();

            var strIndex = list.FirstIndexOf(i =>
                "Current map is null after loading but there are maps available. Setting current map to [0].".Equals(i.operand)
            );

            // Remove Log.Error(str) call and setting value=0
            list.RemoveAt(strIndex);
            list.RemoveAt(strIndex);
            list.RemoveAt(strIndex);
            list.RemoveAt(strIndex);

            return list;
        }
    }

    [HarmonyPatch(typeof(PawnTextureAtlas), MethodType.Constructor)]
    static class PawnTextureAtlasCtorPatch
    {
        static void Postfix(PawnTextureAtlas __instance)
        {
            // Pawn ids can change during deserialization when fixing local (negative) ids in CrossRefHandler_Clear_Patch
            __instance.frameAssignments = new Dictionary<Pawn, PawnTextureAtlasFrameSet>(
                IdentityComparer<Pawn>.Instance
            );
        }
    }

    [HarmonyPatch]
    public static class StoragesKeepsTheirOwners
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.PostExposeData))]
        static void PostCompBiosculpterPod(CompBiosculpterPod __instance)
            => FixStorage(__instance, __instance.allowedNutritionSettings);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CompChangeableProjectile), nameof(CompChangeableProjectile.PostExposeData))]
        static void PostCompChangeableProjectile(CompChangeableProjectile __instance)
            => FixStorage(__instance, __instance.allowedShellsSettings);

        // Fix syncing of copy/paste due to null StorageSettings.owner by assigning the parent
        // in ExposeData. The patched types omit passing/assigning self as the owner by passing
        // Array.Empty<object>() as the argument to expose data on StorageSetting.
        static void FixStorage(IStoreSettingsParent __instance, StorageSettings ___allowedNutritionSettings)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ___allowedNutritionSettings.owner ??= __instance;
        }
    }

    [HarmonyPatch(typeof(MoteAttachLink), nameof(MoteAttachLink.UpdateDrawPos))]
    static class MoteAttachLinkUsesTruePosition
    {
        static void Prefix() => DrawPosPatch.returnTruePosition = true;

        static void Finalizer() => DrawPosPatch.returnTruePosition = false;
    }
}
