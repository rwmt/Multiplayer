using Harmony;
using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;
using Verse.Sound;

namespace Multiplayer.Client
{
    static class HarmonyPatches
    {
        [MpPrefix(typeof(PatchProcessor), nameof(PatchProcessor.Patch))]
        static void PatchProcessorPrefix(List<MethodBase> ___originals)
        {
            foreach (MethodBase m in ___originals)
            {
                MarkNoInlining(m);
            }
        }

        public unsafe static void MarkNoInlining(MethodBase method)
        {
            ushort* iflags = (ushort*)(method.MethodHandle.Value) + 1;
            *iflags |= (ushort)MethodImplOptions.NoInlining;
        }

        static readonly FieldInfo paramName = AccessTools.Field(typeof(ParameterInfo), "NameImpl");

        [MpPrefix(typeof(MethodPatcher), "EmitCallParameter")]
        static void EmitCallParamsPrefix(MethodBase original, MethodInfo patch)
        {
            if (Attribute.GetCustomAttribute(patch, typeof(IndexedPatchParameters)) == null)
                return;

            ParameterInfo[] patchParams = patch.GetParameters();

            for (int i = 0; i < patchParams.Length; i++)
            {
                string name;

                if (original.IsStatic)
                    name = original.GetParameters()[i].Name;
                else if (i == 0)
                    name = MethodPatcher.INSTANCE_PARAM;
                else
                    name = original.GetParameters()[i - 1].Name;

                paramName.SetValue(patchParams[i], name);
            }
        }
    }

    // For instance methods the first parameter is the instance
    // The rest are original method's parameters in order
    [AttributeUsage(AttributeTargets.Method)]
    public class IndexedPatchParameters : Attribute
    {
    }

    [MpPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
    public static class MainMenuMarker
    {
        public static bool drawing;

        static void Prefix() => drawing = true;
        static void Postfix() => drawing = false;
    }

    [HarmonyPatch(typeof(WildAnimalSpawner))]
    [HarmonyPatch(nameof(WildAnimalSpawner.WildAnimalSpawnerTick))]
    public static class WildAnimalSpawnerTickMarker
    {
        public static bool ticking;

        static void Prefix() => ticking = true;
        static void Postfix() => ticking = false;
    }

    [HarmonyPatch(typeof(WildPlantSpawner))]
    [HarmonyPatch(nameof(WildPlantSpawner.WildPlantSpawnerTick))]
    public static class WildPlantSpawnerTickMarker
    {
        public static bool ticking;

        static void Prefix() => ticking = true;
        static void Postfix() => ticking = false;
    }

    [HarmonyPatch(typeof(SteadyEnvironmentEffects))]
    [HarmonyPatch(nameof(SteadyEnvironmentEffects.SteadyEnvironmentEffectsTick))]
    public static class SteadyEnvironmentEffectsTickMarker
    {
        public static bool ticking;

        static void Prefix() => ticking = true;
        static void Postfix() => ticking = false;
    }

    [MpPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
    public static class MainMenuPatch
    {
        static void Prefix(Rect rect, List<ListableOption> optList)
        {
            if (!MainMenuMarker.drawing) return;

            if (Current.ProgramState == ProgramState.Entry)
            {
                int newColony = optList.FindIndex(opt => opt.label == "NewColony".Translate());
                if (newColony != -1)
                {
                    optList.Insert(newColony + 1, new ListableOption("Connect to server", () =>
                    {
                        Find.WindowStack.Add(new ServerBrowser());
                    }));

                    optList.Insert(0, new ListableOption("Load replay", () =>
                    {
                        Multiplayer.LoadReplay();
                    }));
                }
            }

            if (optList.Any(opt => opt.label == "ReviewScenario".Translate()))
            {
                if (Multiplayer.LocalServer == null && Multiplayer.Client == null)
                {
                    optList.Insert(0, new ListableOption("Host a server", () =>
                    {
                        Find.WindowStack.Add(new HostWindow());
                    }));
                }

                if (Multiplayer.Client != null)
                {
                    optList.Insert(0, new ListableOption("Save replay", () =>
                    {
                        /*Stopwatch ticksStart = Stopwatch.StartNew();
                        for (int i = 0; i < 1000; i++)
                        {
                            Find.TickManager.DoSingleTick();
                        }
                        Log.Message("1000 ticks took " + ticksStart.ElapsedMilliseconds + "ms (" + (ticksStart.ElapsedMilliseconds / 1000.0) + ")");
                        */

                        //Multiplayer.SendGameData(Multiplayer.SaveGame());

                        //Multiplayer.LocalServer.Enqueue(() => Multiplayer.LocalServer.DoAutosave());

                        Multiplayer.SaveReplay();
                    }));

                    optList.Insert(0, new ListableOption("Advance time", () =>
                    {
                        SyncPatches.AdvanceTime();
                    }));

                    optList.Insert(0, new ListableOption("Save map", () =>
                    {
                        SyncPatches.SaveMap();
                    }));

                    optList.RemoveAll(opt => opt.label == "Save".Translate() || opt.label == "LoadGame".Translate());

                    optList.Find(opt => opt.label == "QuitToMainMenu".Translate()).action = () =>
                    {
                        OnMainThread.StopMultiplayer();
                        GenScene.GoToMainMenu();
                    };

                    optList.Find(opt => opt.label == "QuitToOS".Translate()).action = () => Root.Shutdown();
                }
            }
        }
    }

    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.RegenerateEverythingNow))]
    public static class MapDrawerRegenPatch
    {
        public static Dictionary<int, MapDrawer> copyFrom = new Dictionary<int, MapDrawer>();

        static bool Prefix(MapDrawer __instance)
        {
            Map map = __instance.map;
            if (!copyFrom.TryGetValue(map.uniqueID, out MapDrawer keepDrawer)) return true;

            map.mapDrawer = keepDrawer;
            keepDrawer.map = map;

            foreach (Section section in keepDrawer.sections)
            {
                section.map = map;

                for (int i = 0; i < section.layers.Count; i++)
                {
                    SectionLayer layer = section.layers[i];

                    if (!ShouldKeep(layer))
                        section.layers[i] = (SectionLayer)Activator.CreateInstance(layer.GetType(), section);
                    else if (layer is SectionLayer_LightingOverlay lighting)
                        lighting.glowGrid = map.glowGrid.glowGrid;
                    else if (layer is SectionLayer_TerrainScatter scatter)
                        scatter.scats.Do(s => s.map = map);
                }
            }

            foreach (Section s in keepDrawer.sections)
                foreach (SectionLayer layer in s.layers)
                    if (!ShouldKeep(layer))
                        layer.Regenerate();

            copyFrom.Remove(map.uniqueID);

            return false;
        }

        static bool ShouldKeep(SectionLayer layer)
        {
            return layer.GetType().Assembly == typeof(Game).Assembly;
        }
    }

    //[HarmonyPatch(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.RebuildAllRegionsAndRooms))]
    public static class RebuildRegionsAndRoomsPatch
    {
        public static Dictionary<int, RegionGrid> copyFrom = new Dictionary<int, RegionGrid>();

        static bool Prefix(RegionAndRoomUpdater __instance)
        {
            Map map = __instance.map;
            if (!copyFrom.TryGetValue(map.uniqueID, out RegionGrid oldRegions)) return true;

            __instance.initialized = true;
            map.temperatureCache.ResetTemperatureCache();

            oldRegions.map = map; // for access to cellIndices in the iterator

            foreach (Region r in oldRegions.AllRegions_NoRebuild_InvalidAllowed)
            {
                r.cachedAreaOverlaps = null;
                r.cachedDangers.Clear();
                r.mark = 0;
                r.reachedIndex = 0;
                r.closedIndex = new uint[RegionTraverser.NumWorkers];
                r.cachedCellCount = -1;
                r.mapIndex = (sbyte)map.Index;

                if (r.door != null)
                    r.door = map.ThingReplacement(r.door);

                foreach (List<Thing> things in r.listerThings.listsByGroup.Concat(r.ListerThings.listsByDef.Values))
                    if (things != null)
                        for (int j = 0; j < things.Count; j++)
                            if (things[j] != null)
                                things[j] = map.ThingReplacement(things[j]);

                Room rm = r.Room;
                if (rm == null) continue;

                rm.mapIndex = (sbyte)map.Index;
                rm.cachedCellCount = -1;
                rm.cachedOpenRoofCount = -1;
                rm.statsAndRoleDirty = true;
                rm.stats = new DefMap<RoomStatDef, float>();
                rm.role = null;
                rm.uniqueNeighbors.Clear();
                rm.uniqueContainedThings.Clear();

                RoomGroup rg = rm.groupInt;
                rg.tempTracker.cycleIndex = 0;
            }

            for (int i = 0; i < oldRegions.regionGrid.Length; i++)
                map.regionGrid.regionGrid[i] = oldRegions.regionGrid[i];

            copyFrom.Remove(map.uniqueID);

            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGrid))]
    public static class WorldGridCtorPatch
    {
        public static WorldGrid copyFrom;

        static bool Prefix(WorldGrid __instance, int ___cachedTraversalDistance, int ___cachedTraversalDistanceForStart, int ___cachedTraversalDistanceForEnd)
        {
            if (copyFrom == null) return true;

            WorldGrid grid = __instance;

            grid.viewAngle = copyFrom.viewAngle;
            grid.viewCenter = copyFrom.viewCenter;
            grid.verts = copyFrom.verts;
            grid.tileIDToNeighbors_offsets = copyFrom.tileIDToNeighbors_offsets;
            grid.tileIDToNeighbors_values = copyFrom.tileIDToNeighbors_values;
            grid.tileIDToVerts_offsets = copyFrom.tileIDToVerts_offsets;
            grid.averageTileSize = copyFrom.averageTileSize;

            grid.tiles = new List<Tile>();
            ___cachedTraversalDistance = -1;
            ___cachedTraversalDistanceForStart = -1;
            ___cachedTraversalDistanceForEnd = -1;

            copyFrom = null;

            return false;
        }
    }

    [HarmonyPatch(typeof(WorldRenderer))]
    public static class WorldRendererCtorPatch
    {
        public static WorldRenderer copyFrom;

        static bool Prefix(WorldRenderer __instance)
        {
            if (copyFrom == null) return true;

            __instance.layers = copyFrom.layers;
            copyFrom = null;

            return false;
        }
    }

    // Fixes a lag spike when opening debug tools
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch(nameof(UIRoot.UIRootOnGUI))]
    static class UIRootPatch
    {
        static bool ran;

        static void Prefix()
        {
            if (ran) return;
            GUI.skin.font = Text.fontStyles[1].font;
            ran = true;
        }
    }

    [HarmonyPatch(typeof(SavedGameLoaderNow))]
    [HarmonyPatch(nameof(SavedGameLoaderNow.LoadGameFromSaveFileNow))]
    [HarmonyPatch(new[] { typeof(string) })]
    public static class LoadPatch
    {
        public static XmlDocument gameToLoad;

        static bool Prefix()
        {
            if (gameToLoad == null) return false;

            bool prevCompress = SaveCompression.doSaveCompression;
            SaveCompression.doSaveCompression = true;

            ScribeUtil.StartLoading(gameToLoad);
            ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);
            Scribe.EnterNode("game");
            Current.Game = new Game();
            Current.Game.LoadGame(); // calls Scribe.loader.FinalizeLoading()

            SaveCompression.doSaveCompression = prevCompress;
            gameToLoad = null;

            Log.Message("Game loaded");

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // Inits all caches
                foreach (ITickable tickable in TickPatch.AllTickables)
                    tickable.Tick();

                if (!Current.Game.Maps.Any())
                {
                    MemoryUtility.UnloadUnusedUnityAssets();
                    Find.World.renderer.RegenerateAllLayersNow();
                }

                /*Find.WindowStack.Add(new CustomSelectLandingSite()
                {
                    nextAct = () => Settle()
                });*/
            });

            return false;
        }

        private static void Settle()
        {
            // notify the server of map gen pause?

            Find.GameInitData.mapSize = 150;
            Find.GameInitData.startingAndOptionalPawns.Add(StartingPawnUtility.NewGeneratedStartingPawn());
            Find.GameInitData.startingAndOptionalPawns.Add(StartingPawnUtility.NewGeneratedStartingPawn());

            Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.SetFaction(Multiplayer.RealPlayerFaction);
            settlement.Tile = Find.GameInitData.startingTile;
            settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
            Find.WorldObjects.Add(settlement);

            IntVec3 intVec = new IntVec3(Find.GameInitData.mapSize, 1, Find.GameInitData.mapSize);
            Map currentMap = MapGenerator.GenerateMap(intVec, settlement, settlement.MapGeneratorDef, settlement.ExtraGenStepDefs, null);
            Find.World.info.initialMapSize = intVec;
            PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.NewColonyOptimism);
            Current.Game.CurrentMap = currentMap;
            Find.CameraDriver.JumpToCurrentMapLoc(MapGenerator.PlayerStartSpot);
            Find.CameraDriver.ResetSize();
            Current.Game.InitData = null;

            Log.Message("New map: " + currentMap.GetUniqueLoadID());
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot))]
    [HarmonyPatch(nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class MainButtonsPatch
    {
        static bool Prefix()
        {
            Text.Font = GameFont.Small;

            {
                int timerLag = (TickPatch.tickUntil - (int)TickPatch.timerInt);
                string text = $"{Find.TickManager.TicksGame} {TickPatch.timerInt} {TickPatch.tickUntil} {timerLag} {Time.deltaTime * 60f}";
                Rect rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text, 330f));
                Widgets.Label(rect, text);
            }

            if (Find.CurrentMap != null && Multiplayer.Client != null)
            {
                MapAsyncTimeComp async = Find.CurrentMap.GetComponent<MapAsyncTimeComp>();
                StringBuilder text = new StringBuilder();
                text.Append(async.mapTicks);

                text.Append($" d: {Find.CurrentMap.designationManager.allDesignations.Count}");

                int faction = Find.CurrentMap.info.parent.Faction.loadID;
                MultiplayerMapComp comp = Find.CurrentMap.GetComponent<MultiplayerMapComp>();
                FactionMapData data = comp.factionMapData.GetValueSafe(faction);

                if (data != null)
                {
                    text.Append($" h: {data.listerHaulables.ThingsPotentiallyNeedingHauling().Count}");
                    text.Append($" sg: {data.haulDestinationManager.AllGroupsListForReading.Count}");
                }

                text.Append($" {Multiplayer.GlobalIdBlock.current}");

                text.Append($"\n{Sync.bufferedChanges.Sum(kv => kv.Value.Count)}");
                text.Append($"\n{async.randState}");
                text.Append($"\n{Multiplayer.WorldComp.randState}");
                text.Append($"\nreal speed: {Find.TickManager.CurTimeSpeed}");

                Rect rect1 = new Rect(80f, 110f, 330f, Text.CalcHeight(text.ToString(), 330f));
                Widgets.Label(rect1, text.ToString());
            }

            if (Multiplayer.IsReplay || TickPatch.skipTo > 0)
            {
                DrawTimeline();
            }

            DoButtons();

            return Find.Maps.Count > 0;
        }

        static void DoButtons()
        {
            float x = 10f;
            const float width = 60f;

            if (Multiplayer.Chat != null)
            {
                if (Widgets.ButtonText(new Rect(Screen.width - width - 10f, x, width, 25f), "Chat"))
                    Find.WindowStack.Add(Multiplayer.Chat);
                x += 25f;
            }

            if (Multiplayer.PacketLog != null)
            {
                if (Widgets.ButtonText(new Rect(Screen.width - width - 10f, x, width, 25f), "Packets"))
                    Find.WindowStack.Add(Multiplayer.PacketLog);
                x += 25f;
            }

            if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any())
            {
                if (Widgets.ButtonText(new Rect(Screen.width - width - 10f, x, width, 25f), "Trading"))
                    Find.WindowStack.Add(new TradingWindow());
                x += 25f;
            }
        }

        static void DrawTimeline()
        {
            const float margin = 50f;
            const float height = 35f;

            Rect rect = new Rect(margin, UI.screenHeight - 35f - height - 10f, UI.screenWidth - margin * 2, height);
            Widgets.DrawBoxSolid(rect, new Color(0.6f, 0.6f, 0.6f, 0.8f));

            int timerEnd = TickPatch.skipTo > 0 ? TickPatch.skipTo : Multiplayer.session.replayTimerEnd;

            double progress = TickPatch.timerInt / timerEnd;
            float progressX = rect.xMin + (float)progress * rect.width;
            Widgets.DrawLine(new Vector2(progressX, rect.yMin), new Vector2(progressX, rect.yMax), Color.green, 7f);

            if (Mouse.IsOver(rect))
            {
                float mouseX = Event.current.mousePosition.x;
                float mouseProgress = (mouseX - rect.xMin) / rect.width;
                int mouseTimer = (int)(timerEnd * mouseProgress);
                Widgets.DrawLine(new Vector2(mouseX, rect.yMin), new Vector2(mouseX, rect.yMax), Color.blue, 3f);

                if (Event.current.type == EventType.MouseDown)
                {
                    if (mouseTimer >= TickPatch.Timer)
                    {
                        TickPatch.skipTo = mouseTimer;
                    }
                    else
                    {
                        ClientJoiningState.ReloadGame(mouseTimer, new List<int>() { 0 }, () =>
                        {
                            TickPatch.tickUntil = Multiplayer.session.replayTimerEnd;
                        });
                    }

                    Event.current.Use();
                }

                ActiveTip tip = new ActiveTip("Tick " + mouseTimer + "\nETA "); // todo eta
                tip.DrawTooltip(GenUI.GetMouseAttachedWindowPos(tip.TipRect.x, tip.TipRect.y));
            }

            Widgets.DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax), Color.white, 4f);
            Widgets.DrawLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), Color.white, 4f);
        }
    }

    [MpPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
    [MpPatch(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI))]
    static class MakeSpaceForReplayTimeline
    {
        static void Prefix()
        {
            if (Multiplayer.IsReplay)
                UI.screenHeight -= 45;
        }

        static void Postfix()
        {
            if (Multiplayer.IsReplay)
                UI.screenHeight += 45;
        }
    }

    [HarmonyPatch(typeof(CaravanArrivalAction_AttackSettlement))]
    [HarmonyPatch(nameof(CaravanArrivalAction_AttackSettlement.Arrived))]
    public static class AttackSettlementPatch
    {
        static bool Prefix(CaravanArrivalAction_AttackSettlement __instance, Caravan caravan)
        {
            if (Multiplayer.Client == null) return true;

            SettlementBase settlement = __instance.settlement;
            if (settlement.Faction.def != Multiplayer.factionDef) return true;

            Multiplayer.Client.Send(Packets.Client_EncounterRequest, new object[] { settlement.Tile });

            return false;
        }
    }

    [HarmonyPatch(typeof(Settlement))]
    [HarmonyPatch(nameof(Settlement.ShouldRemoveMapNow))]
    public static class ShouldRemoveMapPatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(SettlementDefeatUtility))]
    [HarmonyPatch(nameof(SettlementDefeatUtility.CheckDefeated))]
    public static class CheckDefeatedPatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.StartJob))]
    public static class JobTrackerStart
    {
        static void Prefix(Pawn_JobTracker __instance, Job newJob, ref Container<Map> __state)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.ShouldSync)
            {
                Log.Warning($"Started a job {newJob} on pawn {__instance.pawn} from the interface!");
                return;
            }

            Pawn pawn = __instance.pawn;

            __instance.jobsGivenThisTick = 0;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Postfix(Container<Map> __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class JobTrackerEndCurrent
    {
        static void Prefix(Pawn_JobTracker __instance, JobCondition condition, ref Container<Map> __state)
        {
            if (Multiplayer.Client == null) return;
            Pawn pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Postfix(Container<Map> __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.CheckForJobOverride))]
    public static class JobTrackerOverride
    {
        static void Prefix(Pawn_JobTracker __instance, ref Container<Map> __state)
        {
            if (Multiplayer.Client == null) return;
            Pawn pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            ThingContext.Push(pawn);
            __state = pawn.Map;
        }

        static void Postfix(Container<Map> __state)
        {
            if (__state != null)
            {
                __state.PopFaction();
                ThingContext.Pop();
            }
        }
    }

    public static class ThingContext
    {
        private static Stack<Pair<Thing, Map>> stack = new Stack<Pair<Thing, Map>>();

        static ThingContext()
        {
            stack.Push(new Pair<Thing, Map>(null, null));
        }

        public static Thing Current => stack.Peek().First;
        public static Pawn CurrentPawn => Current as Pawn;

        public static Map CurrentMap
        {
            get
            {
                Pair<Thing, Map> peek = stack.Peek();
                if (peek.First != null && peek.First.Map != peek.Second)
                    Log.ErrorOnce("Thing " + peek.First + " has changed its map!", peek.First.thingIDNumber ^ 57481021);
                return peek.Second;
            }
        }

        public static void Push(Thing t)
        {
            stack.Push(new Pair<Thing, Map>(t, t.Map));
        }

        public static void Pop()
        {
            stack.Pop();
        }
    }

    [HarmonyPatch(typeof(GameEnder))]
    [HarmonyPatch(nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class GameEnderPatch
    {
        static bool Prefix() => false;
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

        private static int localIds = -1;

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

            if (Multiplayer.ShouldSync)
                __result = localIds--;
            else
                __result = Multiplayer.GlobalIdBlock.NextId();

            //MpLog.Log("got new id " + __result);

            /*if (currentBlock.current > currentBlock.blockSize * 0.95f && !currentBlock.overflowHandled)
            {
                Multiplayer.Client.Send(Packets.Client_IdBlockRequest, CurrentBlock.mapId);
                currentBlock.overflowHandled = true;
            }*/
        }
    }

    [HarmonyPatch(typeof(PawnComponentsUtility))]
    [HarmonyPatch(nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    public static class AddAndRemoveCompsPatch
    {
        static void Prefix(Pawn pawn, ref Container<Map> __state)
        {
            if (Multiplayer.Client == null || pawn.Faction == null) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Postfix(Pawn pawn, Container<Map> __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Building))]
    [HarmonyPatch(nameof(Building.GetGizmos))]
    public static class GetGizmos
    {
        static void Postfix(Building __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = __result.Concat(new Command_Action
            {
                defaultLabel = "Set faction",
                action = () =>
                {
                    Find.WindowStack.Add(new Dialog_String(s =>
                    {
                        //Type t = typeof(WindowStack).Assembly.GetType("Verse.DataAnalysisTableMaker", true);
                        //MethodInfo m = t.GetMethod(s, BindingFlags.Public | BindingFlags.Static);
                        //m.Invoke(null, new object[0]);
                    }));

                    //__instance.SetFaction(Faction.OfSpacerHostile);
                }
            });
        }
    }

    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch(nameof(Pawn.GetGizmos))]
    public static class PawnGizmos
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = __result.Concat(new Command_Action
            {
                defaultLabel = "Thinker",
                action = () =>
                {
                    Log.Message("touch " + TouchPathEndModeUtility.IsAdjacentOrInsideAndAllowedToTouch(__instance.Position, __instance.Position + new IntVec3(1, 0, 1), __instance.Map));

                    //Find.WindowStack.Add(new ThinkTreeWindow(__instance));
                    // Log.Message("" + Multiplayer.mainBlock.blockStart);
                    // Log.Message("" + __instance.Map.GetComponent<MultiplayerMapComp>().encounterIdBlock.current);
                    //Log.Message("" + __instance.Map.GetComponent<MultiplayerMapComp>().encounterIdBlock.GetHashCode());
                }
            });
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

    [HarmonyPatch(typeof(Dialog_BillConfig))]
    [HarmonyPatch(new[] { typeof(Bill_Production), typeof(IntVec3) })]
    public static class DialogPatch
    {
        static void Postfix(Dialog_BillConfig __instance)
        {
            __instance.absorbInputAroundWindow = false;
        }
    }

    public class Dialog_String : Dialog_Rename
    {
        private Action<string> action;

        public Dialog_String(Action<string> action)
        {
            this.action = action;
        }

        public override void SetName(string name)
        {
            action(name);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.GetGizmos))]
    public static class WorldObjectGizmos
    {
        static void Postfix(WorldObject __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = __result.Concat(new Command_Action
            {
                defaultLabel = "Jump to",
                action = () =>
                {
                    /*if (__instance is Caravan c)
                    {
                        foreach (Pawn p in c.pawns)
                        {
                            Log.Message(p + " " + p.Spawned);

                            foreach (Thing t in p.inventory.innerContainer)
                                Log.Message(t + " " + t.Spawned);

                            foreach (Thing t in p.equipment.AllEquipmentListForReading)
                                Log.Message(t + " " + t.Spawned);

                            foreach (Thing t in p.apparel.GetDirectlyHeldThings())
                                Log.Message(t + " " + t.Spawned);
                        }
                    }*/

                    Find.WindowStack.Add(new Dialog_JumpTo(s =>
                    {
                        int i = int.Parse(s);
                        Find.WorldCameraDriver.JumpTo(i);
                        Find.WorldSelector.selectedTile = i;
                    }));
                }
            });
        }
    }

    [HarmonyPatch(typeof(ListerHaulables))]
    [HarmonyPatch(nameof(ListerHaulables.ListerHaulablesTick))]
    public static class HaulablesTickPatch
    {
        static bool Prefix() => Multiplayer.Client == null || MultiplayerMapComp.tickingFactions;
    }

    [HarmonyPatch(typeof(ResourceCounter))]
    [HarmonyPatch(nameof(ResourceCounter.ResourceCounterTick))]
    public static class ResourcesTickPatch
    {
        static bool Prefix() => Multiplayer.Client == null || MultiplayerMapComp.tickingFactions;
    }

    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.WindowsForcePause), PropertyMethod.Getter)]
    public static class WindowsPausePatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(RandomNumberGenerator_BasicHash))]
    [HarmonyPatch(nameof(RandomNumberGenerator_BasicHash.GetInt))]
    public static class RandPatch
    {
        public static int calls;
        public static List<int> called = new List<int>();
        public static List<List<RandContext>> traces = new List<List<RandContext>>()
        {
            new List<RandContext>()
        };

        public static readonly bool collect = false;

        static void Prefix()
        {
            if (RandPatches.Ignore || Multiplayer.Client == null) return;
            if (Current.ProgramState != ProgramState.Playing && !Multiplayer.reloading) return;
            if (Multiplayer.reloading) return;

            if (MapAsyncTimeComp.tickingMap != null)
            {
                calls++;

                if (ThingContext.Current?.def == ThingDefOf.SteamGeyser) return;
                if (SteadyEnvironmentEffectsTickMarker.ticking || WildAnimalSpawnerTickMarker.ticking || WildPlantSpawnerTickMarker.ticking) return;

                if (!Multiplayer.IsReplay)
                    Log(0);

                //MpLog.Log($"rand {calls}, {ThingContext.Current} {Rand.StateCompressed} {new StackTrace().Hash()}");
            }
        }

        public static void Log(int extra)
        {
            if (!collect) return;

            StackTrace trace = new StackTrace(1);
            int thingHash = ThingContext.Current?.thingIDNumber ?? -1;
            called.Add(trace.Hash().Combine(calls).Combine(thingHash));

            traces[0].Add(new RandContext()
            {
                thing = ThingContext.Current.ToStringSafe(),
                trace = trace,
                calls = calls,
                extra = extra
            });
        }
    }

    public class RandContext
    {
        public string thing;
        public StackTrace trace;
        public int calls;
        public int extra;

        public override string ToString()
        {
            return $"Thing: {thing}, Calls: {calls}, Extra: {extra}\n{trace}";
        }
    }

    [HarmonyPatch(typeof(Rand))]
    [HarmonyPatch(nameof(Rand.Seed), PropertyMethod.Setter)]
    public static class RandSetSeedPatch
    {
        public static bool dontLog;

        static void Prefix()
        {
            if (dontLog) return;
            //if (MapAsyncTimeComp.tickingMap != null)
            //MpLog.Log("set seed");
        }
    }

    [HarmonyPatch(typeof(AutoBuildRoofAreaSetter))]
    [HarmonyPatch(nameof(AutoBuildRoofAreaSetter.TryGenerateAreaNow))]
    public static class AutoRoofPatch
    {
        static bool Prefix(AutoBuildRoofAreaSetter __instance, Room room, ref Map __state)
        {
            if (Multiplayer.Client == null) return true;
            if (room.Dereferenced || room.TouchesMapEdge || room.RegionCount > 26 || room.CellCount > 320 || room.RegionType == RegionType.Portal) return false;

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

    [HarmonyPatch(typeof(Pawn_DrawTracker))]
    [HarmonyPatch(nameof(Pawn_DrawTracker.DrawPos), PropertyMethod.Getter)]
    static class DrawPosPatch
    {
        // Give the root position when ticking
        static void Postfix(Pawn_DrawTracker __instance, ref Vector3 __result)
        {
            if (Multiplayer.Client == null || Multiplayer.ShouldSync) return;
            __result = __result - __instance.tweener.TweenedPos + __instance.tweener.TweenedPosRoot();
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.ExposeData))]
    public static class PawnExposeDataFirst
    {
        public static Container<Map> state;

        // Postfix so Thing's faction is already loaded
        static void Postfix(Thing __instance)
        {
            if (!(__instance is Pawn)) return;
            if (Multiplayer.Client == null || __instance.Faction == null || Find.FactionManager == null || Find.FactionManager.AllFactions.Count() == 0) return;

            ThingContext.Push(__instance);
            state = __instance.Map;
            __instance.Map.PushFaction(__instance.Faction);
        }
    }

    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch(nameof(Pawn.ExposeData))]
    public static class PawnExposeDataLast
    {
        static void Postfix()
        {
            if (PawnExposeDataFirst.state != null)
            {
                PawnExposeDataFirst.state.PopFaction();
                ThingContext.Pop();
                PawnExposeDataFirst.state = null;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_NeedsTracker))]
    [HarmonyPatch(nameof(Pawn_NeedsTracker.AddOrRemoveNeedsAsAppropriate))]
    public static class AddRemoveNeeds
    {
        static void Prefix(Pawn_NeedsTracker __instance)
        {
            //MpLog.Log("add remove needs {0} {1}", FactionContext.OfPlayer.ToString(), __instance.GetPropertyOrField("pawn"));
        }
    }

    [HarmonyPatch(typeof(PawnTweener))]
    [HarmonyPatch(nameof(PawnTweener.PreDrawPosCalculation))]
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

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.TickRateMultiplier), PropertyMethod.Getter)]
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

    [HarmonyPatch(typeof(Log))]
    [HarmonyPatch(nameof(Log.Warning))]
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

    [HarmonyPatch(typeof(UI))]
    [HarmonyPatch(nameof(UI.MouseCell))]
    public static class MouseCellPatch
    {
        public static IntVec3? result;

        static void Postfix(ref IntVec3 __result)
        {
            if (result.HasValue)
                __result = result.Value;
        }
    }

    [HarmonyPatch(typeof(KeyBindingDef))]
    [HarmonyPatch(nameof(KeyBindingDef.IsDownEvent), PropertyMethod.Getter)]
    public static class KeyIsDownPatch
    {
        public static bool? result;
        public static KeyBindingDef forKey;

        static bool Prefix(KeyBindingDef __instance) => !(__instance == forKey && result.HasValue);

        static void Postfix(KeyBindingDef __instance, ref bool __result)
        {
            if (__instance == forKey && result.HasValue)
                __result = result.Value;
        }
    }

    // Fix window focus
    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.CloseWindowsBecauseClicked))]
    public static class WindowFocusPatch
    {
        static void Prefix(Window clickedWindow)
        {
            for (int i = Find.WindowStack.Windows.Count - 1; i >= 0; i--)
            {
                Window window = Find.WindowStack.Windows[i];
                if (window == clickedWindow || window.closeOnClickedOutside) break;
                UI.UnfocusCurrentControl();
            }
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

    // Seed the rotation random
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool) })]
    static class GenSpawnRotatePatch
    {
        static MethodInfo Rot4GetRandom = AccessTools.Property(typeof(Rot4), nameof(Rot4.Random)).GetGetMethod();

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                if (inst.operand == Rot4GetRandom)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Thing), nameof(Thing.thingIDNumber)));
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Rand), nameof(Rand.PushState), new[] { typeof(int) }));
                }

                yield return inst;

                if (inst.operand == Rot4GetRandom)
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Rand), nameof(Rand.PopState)));
            }
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteWhenFinished))]
    static class ExecuteWhenFinishedPatch
    {
        static bool Prefix(Action action)
        {
            if (Multiplayer.simulating)
            {
                action();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    static class SetupQuickTestPatch
    {
        public static bool marker;

        static void Prefix() => marker = true;

        static void Postfix()
        {
            Find.GameInitData.mapSize = 250;
            marker = false;
        }
    }

    [HarmonyPatch(typeof(GameInitData), nameof(GameInitData.ChooseRandomStartingTile))]
    static class RandomStartingTilePatch
    {
        static void Postfix()
        {
            if (SetupQuickTestPatch.marker)
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
            if (SetupQuickTestPatch.marker)
                __result = "multiplayer";
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), "<SortWornApparelIntoDrawOrder>m__0")]
    static class FixApparelSort
    {
        static void Postfix(Apparel a, Apparel b, ref int __result)
        {
            if (__result == 0)
                __result = a.thingIDNumber.CompareTo(b.thingIDNumber);
        }
    }

    [MpPatch(typeof(OutfitDatabase), nameof(OutfitDatabase.GenerateStartingOutfits))]
    [MpPatch(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.GenerateStartingDrugPolicies))]
    [MpPatch(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.GenerateStartingFoodRestrictions))]
    static class CancelReinitializationDuringLoading
    {
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

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.RebuildAll))]
    static class ListerFilthRebuildPatch
    {
        static bool ignore;

        static void Prefix(ListerFilthInHomeArea __instance)
        {
            if (ignore) return;
            ignore = true;
            foreach (FactionMapData data in __instance.map.MpComp().factionMapData.Values)
            {
                __instance.map.PushFaction(data.factionId);
                data.listerFilthInHomeArea.RebuildAll();
                __instance.map.PopFaction();
            }
            ignore = false;
        }
    }

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.Notify_FilthSpawned))]
    static class ListerFilthSpawnedPatch
    {
        static bool ignore;

        static void Prefix(ListerFilthInHomeArea __instance, Filth f)
        {
            if (ignore) return;
            ignore = true;
            foreach (FactionMapData data in __instance.map.MpComp().factionMapData.Values)
            {
                __instance.map.PushFaction(data.factionId);
                data.listerFilthInHomeArea.Notify_FilthSpawned(f);
                __instance.map.PopFaction();
            }
            ignore = false;
        }
    }

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.Notify_FilthDespawned))]
    static class ListerFilthDespawnedPatch
    {
        static bool ignore;

        static void Prefix(ListerFilthInHomeArea __instance, Filth f)
        {
            if (ignore) return;
            ignore = true;
            foreach (FactionMapData data in __instance.map.MpComp().factionMapData.Values)
            {
                __instance.map.PushFaction(data.factionId);
                data.listerFilthInHomeArea.Notify_FilthDespawned(f);
                __instance.map.PopFaction();
            }
            ignore = false;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    static class LoadGamePatch
    {
        public static bool loading;

        static void Prefix() => loading = true;
        static void Postfix() => loading = false;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.ExposeSmallComponents))]
    static class GameExposeComponentsPatch
    {
        static void Prefix()
        {
            if (Multiplayer.Client == null) return;
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            Multiplayer.game = new MultiplayerGame();
        }
    }

    [HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
    static class ClearAllPatch
    {
        static void Postfix()
        {
            Multiplayer.game = null;
        }
    }

    [HarmonyPatch(typeof(FactionManager), nameof(FactionManager.RecacheFactions))]
    static class RecacheFactionsPatch
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.game.dummyFaction = Find.FactionManager.GetById(-1);
        }
    }

    [HarmonyPatch(typeof(World), nameof(World.ExposeComponents))]
    static class WorldExposeComponentsPatch
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.game.worldComp = Find.World.GetComponent<MultiplayerWorldComp>();
        }
    }

    [MpPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot))]
    [MpPatch(typeof(Command_SetPlantToGrow), nameof(Command_SetPlantToGrow.WarnAsAppropriate))]
    [MpPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote), new[] { typeof(IntVec3), typeof(Map), typeof(ThingDef), typeof(float) })]
    [MpPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote), new[] { typeof(Vector3), typeof(Map), typeof(ThingDef), typeof(float) })]
    [MpPatch(typeof(TutorUtility), nameof(TutorUtility.DoModalDialogIfNotKnown))]
    static class CancelFeedbackNotTargetedAtMe
    {
        private static bool Cancel =>
            Multiplayer.Client != null &&
            Multiplayer.ExecutingCmds &&
            !TickPatch.currentExecutingCmdIssuedBySelf;

        static bool Prefix() => !Cancel;
    }

    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), new[] { typeof(Message), typeof(bool) })]
    static class SilenceMessagesNotTargetedAtMe
    {
        static bool Prefix(bool historical)
        {
            bool cancel = Multiplayer.Client != null && !historical && Multiplayer.ExecutingCmds && !TickPatch.currentExecutingCmdIssuedBySelf;
            return !cancel;
        }
    }

    [MpPatch(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) })]
    [MpPatch(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) })]
    static class MessagesMarker
    {
        public static bool? historical;

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

    [HarmonyPatch(typeof(Log))]
    [HarmonyPatch(nameof(Log.ReachedMaxMessagesLimit), PropertyMethod.Getter)]
    static class LogMaxMessagesPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(TutorSystem))]
    [HarmonyPatch(nameof(TutorSystem.AdaptiveTrainingEnabled), PropertyMethod.Getter)]
    static class DisableAdaptiveLearningPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Start))]
    static class RootPlayStartMarker
    {
        public static bool starting;

        static void Prefix() => starting = true;
        static void Postfix() => starting = false;
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>) })]
    static class CancelLongEventDuringReloading
    {
        // Disable standard loading and screen fading during a reload
        static bool Prefix()
        {
            if (RootPlayStartMarker.starting && Multiplayer.reloading) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(LongEventHandler.QueuedLongEvent))]
    [HarmonyPatch(nameof(LongEventHandler.QueuedLongEvent.UseStandardWindow), PropertyMethod.Getter)]
    static class ShowStandardWindow
    {
        static void Postfix(LongEventHandler.QueuedLongEvent __instance, ref bool __result)
        {
            if (__instance.eventTextKey == "MpSimulating")
                __result = true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.ImmediateWindow))]
    static class LongEventWindowAbsorbInput
    {
        public const int LongEventWindowId = 62893994;

        static void Prefix(int ID, ref bool absorbInputAroundWindow)
        {
            if (ID == LongEventWindowId)
                absorbInputAroundWindow = true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.AddNewImmediateWindow))]
    static class LongEventWindowPreventCameraMotion
    {
        static void Postfix(int ID)
        {
            if (ID == -LongEventWindowAbsorbInput.LongEventWindowId)
                Find.WindowStack.windows.Find(w => w.ID == ID).preventCameraMotion = true;
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.WindowOnGUI))]
    static class LongEventWindowBackground
    {
        static void Prefix(Window __instance)
        {
            if (__instance.ID == -LongEventWindowAbsorbInput.LongEventWindowId ||
                __instance is DisconnectedWindow ||
                __instance is MpFormingCaravanWindow
            )
                Widgets.DrawBoxSolid(new Rect(0, 0, UI.screenWidth, UI.screenHeight), new Color(0, 0, 0, 0.5f));
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryGetMeleeVerb))]
    static class TryGetMeleeVerbPatch
    {
        static bool Cancel => Multiplayer.Client != null && Multiplayer.ShouldSync;

        static bool Prefix()
        {
            // Namely FloatMenuUtility.GetMeleeAttackAction
            return !Cancel;
        }

        static void Postfix(Pawn_MeleeVerbs __instance, Thing target, ref Verb __result)
        {
            if (Cancel)
            {
                __result =
                    __instance.GetUpdatedAvailableVerbsList(false).FirstOrDefault(ve => ve.GetSelectionWeight(target) != 0).verb ??
                    __instance.GetUpdatedAvailableVerbsList(true).FirstOrDefault(ve => ve.GetSelectionWeight(target) != 0).verb;
            }
        }
    }

    [HarmonyPatch(typeof(ThingWithComps))]
    [HarmonyPatch(nameof(ThingWithComps.InitializeComps))]
    static class InitializeCompsPatch
    {
        static void Postfix(ThingWithComps __instance)
        {
            if (__instance is Pawn)
            {
                MultiplayerPawnComp comp = new MultiplayerPawnComp() { parent = __instance };
                __instance.AllComps.Add(comp);
            }
        }
    }

    public class MultiplayerPawnComp : ThingComp
    {
        public SituationalThoughtHandler thoughtsForInterface;
    }

    [HarmonyPatch(typeof(IncidentWorker_PawnsArrive), nameof(IncidentWorker_PawnsArrive.FactionCanBeGroupSource))]
    static class FactionCanBeGroupSourcePatch
    {
        static void Postfix(Faction f, ref bool __result)
        {
            __result &= f.def.pawnGroupMakers?.Count > 0;
        }
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

            insts.Insert(insts.Count - 1, new CodeInstruction(OpCodes.Ldloc_2));
            insts.Insert(insts.Count - 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GenerateNewPawnInternalPatch), nameof(Unshuffle)).MakeGenericMethod(typeof(NameTriple))));

            return insts;
        }

        public static void Unshuffle<T>(List<T> list)
        {
            uint iters = Rand.iterations;

            int i = 0;
            while (i < list.Count)
            {
                int index = Mathf.Abs(Rand.random.GetInt(iters--) % (i + 1));
                T value = list[index];
                list[index] = list[i];
                list[i] = value;
                i++;
            }
        }
    }

    [HarmonyPatch(typeof(GlowGrid))]
    [HarmonyPatch(new[] { typeof(Map) })]
    static class GlowGridCtorPatch
    {
        static void Postfix(GlowGrid __instance)
        {
            __instance.litGlowers = new HashSet<CompGlower>(new CompGlowerEquality());
        }

        class CompGlowerEquality : IEqualityComparer<CompGlower>
        {
            public bool Equals(CompGlower x, CompGlower y) => x == y;
            public int GetHashCode(CompGlower obj) => obj.parent.thingIDNumber;
        }
    }

    [HarmonyPatch(typeof(ThingCategoryDef))]
    [HarmonyPatch(nameof(ThingCategoryDef.DescendantThingDefs), PropertyMethod.Getter)]
    static class ThingCategoryDef_DescendantThingDefs
    {
        static Dictionary<ThingCategoryDef, HashSet<ThingDef>> values = new Dictionary<ThingCategoryDef, HashSet<ThingDef>>();

        static bool Prefix(ThingCategoryDef __instance)
        {
            return !values.ContainsKey(__instance);
        }

        static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingDef> __result)
        {
            if (values.TryGetValue(__instance, out HashSet<ThingDef> set))
            {
                __result = set;
                return;
            }

            set = new HashSet<ThingDef>(__result);
            values[__instance] = set;
            __result = set;
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
            Log.Message("Uniq ids " + Multiplayer.GlobalIdBlock.current);
        }

        public static void SetupMap(Map map)
        {
            Log.Message("Uniq ids " + Multiplayer.GlobalIdBlock.current);
            Log.Message("new map " + map.uniqueID);

            MapAsyncTimeComp async = map.AsyncTime();
            MultiplayerMapComp mapComp = map.MpComp();
            Faction dummyFaction = Multiplayer.DummyFaction;

            mapComp.factionMapData[map.ParentFaction.loadID] = FactionMapData.FromMap(map);

            mapComp.factionMapData[dummyFaction.loadID] = FactionMapData.New(dummyFaction.loadID, map);
            mapComp.factionMapData[dummyFaction.loadID].areaManager.AddStartingAreas();

            async.storyteller = new Storyteller(Find.Storyteller.def, Find.Storyteller.difficulty);
        }
    }

    [HarmonyPatch(typeof(WorldObjectSelectionUtility), nameof(WorldObjectSelectionUtility.VisibleToCameraNow))]
    static class CaravanVisibleToCameraPatch
    {
        static void Postfix(ref bool __result)
    {
            if (!Multiplayer.ShouldSync)
                __result = false;
    }
    }

}