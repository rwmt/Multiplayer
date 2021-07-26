using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Desyncs;
using Multiplayer.Common;
using RestSharp;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;
using Verse.Sound;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(GameEnder))]
    [HarmonyPatch(nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class GameEnderPatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(UniqueIDsManager))]
    [HarmonyPatch(nameof(UniqueIDsManager.GetNextID))]
    public static class UniqueIdsPatch
    {
        private static IdBlock currentBlock;

        public static IdBlock CurrentBlock
        {
            get => currentBlock;

            set
            {
                if (value != null && currentBlock != null && currentBlock != value)
                    Log.Warning("Reassigning the current id block!");
                currentBlock = value;
            }
        }

        private static int localIds = -2;

        static bool Prefix()
        {
            return Multiplayer.Client == null || !Multiplayer.InInterface;
        }

        static void Postfix(ref int __result)
        {
            if (Multiplayer.Client == null) return;

            /*IdBlock currentBlock = CurrentBlock;
            if (currentBlock == null)
            {
                __result = localIds--;
                if (!Multiplayer.ShouldSync)
                    Log.Warning("Tried to get a unique id without an id block set!");
                return;
            }

            __result = currentBlock.NextId();*/

            if (Multiplayer.InInterface)
            {
                __result = localIds--;
            }
            else
            {
                __result = Multiplayer.GlobalIdBlock.NextId();
            }

            //MpLog.Log("got new id " + __result);

            /*if (currentBlock.current > currentBlock.blockSize * 0.95f && !currentBlock.overflowHandled)
            {
                Multiplayer.Client.Send(Packets.Client_IdBlockRequest, CurrentBlock.mapId);
                currentBlock.overflowHandled = true;
            }*/
        }
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

    [HarmonyPatch]
    public static class WidgetsResolveParsePatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), nameof(Widgets.ResolveParseNow)).MakeGenericMethod(typeof(int));
        }

        // Fix input field handling
        static void Prefix(bool force, ref int val, ref string buffer, ref string edited)
        {
            if (force)
                edited = Widgets.ToStringTypedIn(val);
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

    [HarmonyPatch(typeof(AutoBuildRoofAreaSetter))]
    [HarmonyPatch(nameof(AutoBuildRoofAreaSetter.TryGenerateAreaNow))]
    public static class AutoRoofPatch
    {
        static bool Prefix(AutoBuildRoofAreaSetter __instance, Room room, ref Map __state)
        {
            if (Multiplayer.Client == null) return true;
            if (room.Dereferenced || room.TouchesMapEdge || room.RegionCount > 26 || room.CellCount > 320 || room.IsDoorway) return false;

            Map map = room.Map;
            Faction faction = null;

            foreach (IntVec3 cell in room.BorderCells)
            {
                Thing holder = cell.GetRoofHolderOrImpassable(map);
                if (holder == null || holder.Faction == null) continue;
                if (faction != null && holder.Faction != faction) return false;
                faction = holder.Faction;
            }

            if (faction == null) return false;

            map.PushFaction(faction);
            __state = map;

            return true;
        }

        static void Postfix(ref Map __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(PawnTweener))]
    [HarmonyPatch(nameof(PawnTweener.TweenedPos), MethodType.Getter)]
    static class DrawPosPatch
    {
        static bool Prefix() => Multiplayer.Client == null || Multiplayer.InInterface;

        // Give the root position during ticking
        static void Postfix(PawnTweener __instance, ref Vector3 __result)
        {
            if (Multiplayer.Client == null || Multiplayer.InInterface) return;
            __result = __instance.TweenedPosRoot();
        }
    }

    /*[HotSwappable]
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

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    public static class TickRatePatch
    {
        static bool Prefix(TickManager __instance, ref float __result)
        {
            if (Multiplayer.Client == null) return true;

            if (__instance.CurTimeSpeed == TimeSpeed.Paused)
                __result = 0;
            else if (__instance.slower.ForcedNormalSpeed)
                __result = 1;
            else if (__instance.CurTimeSpeed == TimeSpeed.Fast)
                __result = 3;
            else if (__instance.CurTimeSpeed == TimeSpeed.Superfast)
                __result = 6;
            else
                __result = 1;

            return false;
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
        public static bool respawningAfterLoad;

        static void Prefix(bool respawningAfterLoad)
        {
            PawnSpawnSetupMarker.respawningAfterLoad = respawningAfterLoad;
        }

        static void Postfix()
        {
            respawningAfterLoad = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.ResetToCurrentPosition))]
    static class PatherResetPatch
    {
        static bool Prefix() => !PawnSpawnSetupMarker.respawningAfterLoad;
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    static class SetupQuickTestPatch
    {
        public static bool marker;

        static void Prefix() => marker = true;

        static void Postfix()
        {
            if (MpVersion.IsDebug)
                Find.GameInitData.mapSize = 250;
            marker = false;
        }
    }

    [HarmonyPatch(typeof(GameInitData), nameof(GameInitData.ChooseRandomStartingTile))]
    static class RandomStartingTilePatch
    {
        static void Postfix()
        {
            if (MpVersion.IsDebug && SetupQuickTestPatch.marker)
            {
                Find.GameInitData.startingTile = 501;
                Find.WorldGrid[Find.GameInitData.startingTile].hilliness = Hilliness.SmallHills;
            }
        }
    }

    [HarmonyPatch(typeof(GenText), nameof(GenText.RandomSeedString))]
    static class GrammarRandomStringPatch
    {
        static void Postfix(ref string __result)
        {
            if (MpVersion.IsDebug && SetupQuickTestPatch.marker)
                __result = "multiplayer1";
        }
    }

    [HarmonyPatch]
    static class FixApparelSort
    {
        static MethodBase TargetMethod() =>
            typeof(Pawn_ApparelTracker).
            GetNestedTypes(BindingFlags.NonPublic).
            Select(t => Inner(t)).NotNull().FirstOrDefault();

        private static MethodBase Inner(Type t)
        {
            if (!t.IsCompilerGenerated())
                return null;
            return AccessTools.FirstMethod(t, m => m.Name.Contains(nameof(Pawn_ApparelTracker.SortWornApparelIntoDrawOrder)));
        }

        static void Postfix(Apparel a, Apparel b, ref int __result)
        {
            if (__result == 0)
                __result = a.thingIDNumber.CompareTo(b.thingIDNumber);
        }
    }

    [HarmonyPatch]
    static class CancelReinitializationDuringLoading
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(OutfitDatabase), nameof(OutfitDatabase.GenerateStartingOutfits));
            yield return AccessTools.Method(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.GenerateStartingDrugPolicies));
            yield return AccessTools.Method(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.GenerateStartingFoodRestrictions));
        }

        static bool Prefix() => Scribe.mode != LoadSaveMode.LoadingVars;
    }

    [HarmonyPatch(typeof(OutfitDatabase), nameof(OutfitDatabase.MakeNewOutfit))]
    static class OutfitUniqueIdPatch
    {
        static void Postfix(Outfit __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.uniqueId = Multiplayer.GlobalIdBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.MakeNewDrugPolicy))]
    static class DrugPolicyUniqueIdPatch
    {
        static void Postfix(DrugPolicy __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.uniqueId = Multiplayer.GlobalIdBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.MakeNewFoodRestriction))]
    static class FoodRestrictionUniqueIdPatch
    {
        static void Postfix(FoodRestriction __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.id = Multiplayer.GlobalIdBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    static class LoadGameMarker
    {
        public static bool loading;

        static void Prefix() => loading = true;
        static void Postfix() => loading = false;
    }

    [HarmonyPatch]
    static class MessagesMarker
    {
        public static bool? historical;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) });
            yield return AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) });
        }

        static void Prefix(bool historical) => MessagesMarker.historical = historical;
        static void Postfix() => historical = null;
    }

    [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextMessageID))]
    static class NextMessageIdPatch
    {
        static int nextUniqueUnhistoricalMessageId = -1;

        static bool Prefix() => !MessagesMarker.historical.HasValue || MessagesMarker.historical.Value;

        static void Postfix(ref int __result)
        {
            if (MessagesMarker.historical.HasValue && !MessagesMarker.historical.Value)
                __result = nextUniqueUnhistoricalMessageId--;
        }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Start))]
    static class RootPlayStartMarker
    {
        public static bool starting;

        static void Prefix() => starting = true;
        static void Postfix() => starting = false;
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool) })]
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
        static bool Prefix() => !LongEventHandler.eventQueue.Any(e => e.eventTextKey == "MpLoading");
    }

    [HarmonyPatch(typeof(ScreenFader), nameof(ScreenFader.StartFade))]
    static class DisableScreenFade2
    {
        static bool Prefix() => !LongEventHandler.eventQueue.Any(e => e.eventTextKey == "MpLoading");
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryGetMeleeVerb))]
    static class TryGetMeleeVerbPatch
    {
        static bool Cancel => Multiplayer.Client != null && Multiplayer.InInterface;

        static bool Prefix()
        {
            // Namely FloatMenuUtility.GetMeleeAttackAction
            return !Cancel;
        }

        static void Postfix(Pawn_MeleeVerbs __instance, Thing target, ref Verb __result)
        {
            if (Cancel)
                __result = __instance.GetUpdatedAvailableVerbsList(false).FirstOrDefault(ve => ve.GetSelectionWeight(target) != 0).verb;
        }
    }

    [HarmonyPatch(typeof(ThingGrid), nameof(ThingGrid.Register))]
    static class DontEnlistNonSaveableThings
    {
        static bool Prefix(Thing t) => t.def.isSaveable;
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.InitializeComps))]
    static class InitializeCompsPatch
    {
        static void Postfix(ThingWithComps __instance)
        {
            if (__instance is Pawn)
            {
                MultiplayerPawnComp comp = new MultiplayerPawnComp() {parent = __instance};
                __instance.AllComps.Add(comp);
            }
        }
    }

    public class MultiplayerPawnComp : ThingComp
    {
        public SituationalThoughtHandler thoughtsForInterface;
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RandomPreferredName))]
    static class PreferredNamePatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.TryGetRandomUnusedSolidName))]
    static class GenerateNewPawnInternalPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            List<CodeInstruction> insts = new List<CodeInstruction>(e);

            insts.Insert(
                insts.Count - 1,
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GenerateNewPawnInternalPatch), nameof(Unshuffle)).MakeGenericMethod(typeof(NameTriple)))
            );

            return insts;
        }

        public static void Unshuffle<T>(List<T> list)
        {
            uint iters = Rand.iterations;

            int i = 0;
            while (i < list.Count)
            {
                int index = Mathf.Abs(MurmurHash.GetInt(Rand.seed, iters--) % (i + 1));
                T value = list[index];
                list[index] = list[i];
                list[i] = value;
                i++;
            }
        }
    }

    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    static class BeforeMapGeneration
    {
        static void Prefix(ref Action<Map> extraInitBeforeContentGen)
        {
            if (Multiplayer.Client == null) return;
            extraInitBeforeContentGen += SetupMap;
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            Log.Message("Unique ids " + Multiplayer.GlobalIdBlock.current);
            Log.Message("Rand " + Rand.StateCompressed);
        }

        public static void SetupMap(Map map)
        {
            Log.Message("New map " + map.uniqueID);
            Log.Message("Uniq ids " + Multiplayer.GlobalIdBlock.current);
            Log.Message("Rand " + Rand.StateCompressed);

            var async = new AsyncTimeComp(map);
            Multiplayer.game.asyncTimeComps.Add(async);

            var mapComp = new MultiplayerMapComp(map);
            Multiplayer.game.mapComps.Add(mapComp);

            InitFactionDataFromMap(map, Faction.OfPlayer);
            InitNewMapFactionData(map, Multiplayer.DummyFaction);

            async.mapTicks = Find.Maps.Where(m => m != map).Select(m => m.AsyncTime()?.mapTicks).Max() ?? Find.TickManager.TicksGame;
            async.storyteller = new Storyteller(Find.Storyteller.def, Find.Storyteller.difficultyDef, Find.Storyteller.difficulty);
            async.storyWatcher = new StoryWatcher();

            if (!MultiplayerWorldComp.asyncTime)
                async.TimeSpeed = Find.TickManager.CurTimeSpeed;
        }

        public static void InitFactionDataFromMap(Map map, Faction f)
        {
            var mapComp = map.MpComp();
            mapComp.factionData[f.loadID] = FactionMapData.NewFromMap(map, f.loadID);

            var customData = mapComp.customFactionData[f.loadID] = CustomFactionMapData.New(f.loadID, map);

            foreach (var t in map.listerThings.AllThings)
                if (t is ThingWithComps tc &&
                    tc.GetComp<CompForbiddable>() is CompForbiddable comp &&
                    !comp.forbiddenInt)
                    customData.unforbidden.Add(t);
        }

        public static void InitNewMapFactionData(Map map, Faction f)
        {
            var mapComp = map.MpComp();

            mapComp.factionData[f.loadID] = FactionMapData.New(f.loadID, map);
            mapComp.factionData[f.loadID].areaManager.AddStartingAreas();

            mapComp.customFactionData[f.loadID] = CustomFactionMapData.New(f.loadID, map);
        }
    }

    [HarmonyPatch(typeof(WorldObjectSelectionUtility), nameof(WorldObjectSelectionUtility.VisibleToCameraNow))]
    static class CaravanVisibleToCameraPatch
    {
        static void Postfix(ref bool __result)
        {
            if (!Multiplayer.InInterface)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_RansomDemand), nameof(IncidentWorker_RansomDemand.CanFireNowSub))]
    static class CancelIncidents
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
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

    [HarmonyPatch]
    static class NoCameraJumpingDuringSkipping
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TrySelect));
            yield return AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TryJumpAndSelect));
            yield return AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TryJump), new[] {typeof(GlobalTargetInfo)});
        }
        static bool Prefix() => !TickPatch.Skipping;
    }

    [HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
    static class WealthWatcherRecalc
    {
        static bool Prefix() => Multiplayer.Client == null || !Multiplayer.ShouldSync;
    }

    [HarmonyPatch(typeof(FloodFillerFog), nameof(FloodFillerFog.FloodUnfog))]
    static class FloodUnfogPatch
    {
        static void Postfix(ref FloodUnfogResult __result)
        {
            if (Multiplayer.Client != null)
                __result.allOnScreen = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawTrackerTick))]
    static class DrawTrackerTickPatch
    {
        static MethodInfo CellRectContains = AccessTools.Method(typeof(CellRect), nameof(CellRect.Contains));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == CellRectContains)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Archive), nameof(Archive.Add))]
    static class ArchiveAddPatch
    {
        static bool Prefix(IArchivable archivable)
        {
            if (Multiplayer.Client == null) return true;

            if (archivable is Message msg && msg.ID < 0)
                return false;
            else if (archivable is Letter letter && letter.ID < 0)
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool) })]
    static class MarkLongEvents
    {
        private static MethodInfo MarkerMethod = AccessTools.Method(typeof(MarkLongEvents), nameof(Marker));

        static void Prefix(ref Action action)
        {
            if (Multiplayer.Client != null && (Multiplayer.Ticking || Multiplayer.ExecutingCmds))
            {
                action += Marker;
            }
        }

        private static void Marker()
        {
        }

        public static bool IsTickMarked(Action action)
        {
            return action?.GetInvocationList()?.Any(d => d.Method == MarkerMethod) ?? false;
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    static class NewLongEvent
    {
        public static bool currentEventWasMarked;

        static void Prefix(ref bool __state)
        {
            __state = LongEventHandler.currentEvent == null;
            currentEventWasMarked = MarkLongEvents.IsTickMarked(LongEventHandler.currentEvent?.eventAction);
        }

        static void Postfix(bool __state)
        {
            currentEventWasMarked = false;

            if (Multiplayer.Client == null) return;

            if (__state && MarkLongEvents.IsTickMarked(LongEventHandler.currentEvent?.eventAction))
                Multiplayer.Client.Send(Packets.Client_Pause, new object[] {true});
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteToExecuteWhenFinished))]
    static class LongEventEnd
    {
        static void Postfix()
        {
            if (Multiplayer.Client != null && NewLongEvent.currentEventWasMarked)
                Multiplayer.Client.Send(Packets.Client_Pause, new object[] {false});
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool) })]
    static class LongEventAlwaysSync
    {
        static void Prefix(ref bool doAsynchronously)
        {
            if (Multiplayer.ExecutingCmds)
                doAsynchronously = false;
        }
    }

    [HarmonyPatch(typeof(Zone), nameof(Zone.Cells), MethodType.Getter)]
    static class ZoneCellsShufflePatch
    {
        static FieldInfo CellsShuffled = AccessTools.Field(typeof(Zone), nameof(Zone.cellsShuffled));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            bool found = false;

            foreach (var inst in insts)
            {
                yield return inst;

                if (!found && inst.operand == CellsShuffled)
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ZoneCellsShufflePatch), nameof(ShouldShuffle)));
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.Or);
                    found = true;
                }
            }
        }

        static bool ShouldShuffle()
        {
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.StartOrResumeBillJob))]
    static class StartOrResumeBillPatch
    {
        static FieldInfo LastFailTicks = AccessTools.Field(typeof(Bill), nameof(Bill.lastIngredientSearchFailTicks));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, MethodBase original)
        {
            var list = new List<CodeInstruction>(insts);

            int index = new CodeFinder(original, list).Forward(OpCodes.Stfld, LastFailTicks).Advance(-1);
            if (list[index].opcode != OpCodes.Ldc_I4_0)
                throw new Exception("Wrong code");

            list.RemoveAt(index);

            list.Insert(
                index,
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StartOrResumeBillPatch), nameof(Value)))
            );

            return list;
        }

        static int Value(Bill bill, Pawn pawn)
        {
            return FloatMenuMakerMap.makingFor == pawn ? bill.lastIngredientSearchFailTicks : 0;
        }
    }

    [HarmonyPatch]
    static class SortArchivablesById
    {
        static MethodBase TargetMethod()
        {
            // Get all non public types, including compiler types
            List<Type> nestedPrivateTypes = new List<Type>(typeof(Archive).GetNestedTypes(BindingFlags.NonPublic));

            // There are two of these in 1.1, <GetGizmos> and <>c. Pluck the one we want for sort inner
            Type cType = nestedPrivateTypes.Find(t => t.Name.Equals("<>c"));

            return AccessTools.Method(cType, "<Add>b__6_0");
        }

        static void Postfix(IArchivable x, ref int __result)
        {
            if (x is ArchivedDialog dialog)
                __result = dialog.ID;
            else if (x is Letter letter)
                __result = letter.ID;
            else if (x is Message msg)
                __result = msg.ID;
        }
    }

    [HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    static class DangerRatingPatch
    {
        static bool Prefix() => !Multiplayer.InInterface;

        static void Postfix(DangerWatcher __instance, ref StoryDanger __result)
        {
            if (Multiplayer.InInterface)
                __result = __instance.dangerRatingInt;
        }
    }

    [HarmonyPatch(typeof(Selector), nameof(Selector.Deselect))]
    static class SelectorDeselectPatch
    {
        public static List<object> deselected;

        static void Prefix(object obj)
        {
            if (deselected != null)
                deselected.Add(obj);
        }
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

            // Ignore pause
            if (__result && __instance.active && WorldRendererUtility.WorldRenderedNow)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.ImmobilizedByMass), MethodType.Getter)]
    static class ImmobilizedByMass_Patch
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    static class CancelSyncDuringPawnGeneration
    {
        static void Prefix() => Multiplayer.dontSync = true;
        static void Postfix() => Multiplayer.dontSync = false;
    }

    [HarmonyPatch(typeof(StoryWatcher_PopAdaptation), nameof(StoryWatcher_PopAdaptation.Notify_PawnEvent))]
    static class CancelStoryWatcherEventInInterface
    {
        static bool Prefix() => !Multiplayer.InInterface;
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

    [HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.AreaRestriction), MethodType.Setter)]
    static class AreaRestrictionSameValue
    {
        [HarmonyPriority(MpPriority.MpFirst + 1)]
        static bool Prefix(Pawn_PlayerSettings __instance, Area value) => __instance.AreaRestriction != value;
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

            foreach(var part in Find.QuestManager.quests.SelectMany(q => q.parts).OfType<QuestPart_Choice>()) {
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

        // Registered in SyncHandlers.cs SyncPatches
        internal static void Choose(QuestPart_Choice part, int index)
        {
            part.Choose(part.choices[index]);
        }
    }

    [HarmonyPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote))]
    [HarmonyPatch(new[] {typeof(Vector3), typeof(Map), typeof(ThingDef), typeof(float)})]
    static class FixNullMotes
    {
        static Dictionary<Type, Mote> cache = new Dictionary<Type, Mote>();

        static void Postfix(ThingDef moteDef, ref Mote __result) {
            if (__result != null) return;

            if (moteDef.mote.needsMaintenance) return;

            var thingClass = moteDef.thingClass;

            if (cache.TryGetValue(thingClass, out Mote value)) {
                __result = value;
            } else {
                __result = (Mote) Activator.CreateInstance(thingClass);
                
                cache.Add(thingClass, __result);
            }

            __result.def = moteDef;
        }
    }

    [HarmonyPatch(typeof(DiaOption), nameof(DiaOption.Activate))]
    static class NodeTreeDialogSync
    {
        static bool Prefix(DiaOption __instance)
        {
            if (Multiplayer.session == null || !Sync.isDialogNodeTreeOpen || !(__instance.dialog is Dialog_NodeTree dialog))
            {
                Sync.isDialogNodeTreeOpen = false;
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
                    Sync.isDialogNodeTreeOpen = false; // Prevents infinite loop, otherwise PreSyncDialog would call this method over and over again
                    var option = dialog.curNode.options[position]; // Get the correct DiaOption
                    option.Activate(); // Call the Activate method to actually "press" the button

                    if (!option.resolveTree) Sync.isDialogNodeTreeOpen = true; // In case dialog is still open, we mark it as such

                    // Try opening the trading menu if the picked option was supposed to do so (caravan meeting, trading option)
                    if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any(t => t.trader is Caravan))
                        Find.WindowStack.Add(new TradingWindow());
                }
                else Sync.isDialogNodeTreeOpen = false;
            }
            else Sync.isDialogNodeTreeOpen = false;
        }
    }

    [HarmonyPatch(typeof(Dialog_NodeTree), nameof(Dialog_NodeTree.PostClose))]
    static class NodeTreeDialogMarkClosed
    {
        // Set the dialog as closed in here as well just in case
        static void Prefix() => Sync.isDialogNodeTreeOpen = false;
    }

    // todo: needed for multifaction
    /*[HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    static class CheckDefeatedPatch
    {
        static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(MapParent), nameof(MapParent.CheckRemoveMapNow))]
    static class CheckRemoveMapNowPatch
    {
        static bool Prefix()
        {
            return false;
        }
    }*/


}
