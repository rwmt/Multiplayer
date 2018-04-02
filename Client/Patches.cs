using Harmony;
using RimWorld;
using RimWorld.Planet;
using Multiplayer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;
using Multiplayer.Common;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(OptionListingUtility))]
    [HarmonyPatch(nameof(OptionListingUtility.DrawOptionListing))]
    public static class MainMenuPatch
    {
        public static Stopwatch time = new Stopwatch();

        static void Prefix(Rect rect, List<ListableOption> optList)
        {
            int newColony = optList.FindIndex(opt => opt.label == "NewColony".Translate());
            if (newColony != -1)
                optList.Insert(newColony + 1, new ListableOption("Connect to server", () =>
                {
                    Find.WindowStack.Add(new ConnectWindow());
                }));

            int reviewScenario = optList.FindIndex(opt => opt.label == "ReviewScenario".Translate());
            if (reviewScenario != -1)
            {
                AddHostButton(optList);

                optList.Insert(0, new ListableOption("Autosave", () =>
                {
                    /*Stopwatch ticksStart = Stopwatch.StartNew();
                    for (int i = 0; i < 1000; i++)
                    {
                        Find.TickManager.DoSingleTick();
                    }
                    Log.Message("1000 ticks took " + ticksStart.ElapsedMilliseconds + "ms (" + (ticksStart.ElapsedMilliseconds / 1000.0) + ")");
                    */

                    //Multiplayer.SendGameData(Multiplayer.SaveGame());

                    Multiplayer.localServer.DoAutosave();
                }));

                optList.Insert(0, new ListableOption("Reload", () =>
                {
                    LongEventHandler.QueueLongEvent(() =>
                    {
                        time = Stopwatch.StartNew();

                        //Multiplayer.init_profiler();
                        //Multiplayer.start_profiler();

                        BetterSaver.doBetterSave = true;
                        Prefs.PauseOnLoad = false;

                        WorldGridCtorPatch.copyFrom = Find.WorldGrid;
                        WorldRendererCtorPatch.copyFrom = Find.World.renderer;

                        LoadPatch.gameToLoad = Multiplayer.SaveGame();

                        MemoryUtility.ClearAllMapsAndWorld();

                        Prefs.LogVerbose = true;
                        SavedGameLoader.LoadGameFromSaveFile("server");
                        Prefs.LogVerbose = false;

                        BetterSaver.doBetterSave = false;

                        //Multiplayer.pause_profiler();
                        //Multiplayer.print_profiler("profiler_reload.txt");

                        Log.Message("saved " + time.ElapsedMilliseconds);
                    }, "Reloading", false, null);
                }));
            }

            if (Multiplayer.client != null && Multiplayer.localServer == null)
                optList.RemoveAll(opt => opt.label == "Save".Translate());
        }

        public static void AddHostButton(List<ListableOption> buttons)
        {
            if (Multiplayer.localServer != null)
                buttons.Insert(0, new ListableOption("Server info", () =>
                {
                    Find.WindowStack.Add(new ServerInfoWindow());
                }));
            else if (Multiplayer.client == null)
                buttons.Insert(0, new ListableOption("Host a server", () =>
                {
                    Find.WindowStack.Add(new HostWindow());
                }));
        }
    }

    [HarmonyPatch(typeof(MapDrawer))]
    [HarmonyPatch(nameof(MapDrawer.RegenerateEverythingNow))]
    public static class MapDrawerRegenPatch
    {
        public static Dictionary<string, MapDrawer> copyFrom = new Dictionary<string, MapDrawer>();

        static FieldInfo mapField = AccessTools.Field(typeof(MapDrawer), "map");
        static FieldInfo sectionsField = AccessTools.Field(typeof(MapDrawer), "sections");

        static bool Prefix(MapDrawer __instance)
        {
            Map map = (Map)mapField.GetValue(__instance);
            if (!copyFrom.TryGetValue(map.GetUniqueLoadID(), out MapDrawer oldDrawer)) return true;

            Section[,] oldSections = (Section[,])sectionsField.GetValue(oldDrawer);
            foreach (Section s in oldSections)
                s.map = map;
            sectionsField.SetValue(__instance, oldSections);

            copyFrom.Remove(map.GetUniqueLoadID());

            return false;
        }
    }

    public static class WorldGridCtorPatch
    {
        public static WorldGrid copyFrom;

        static FieldInfo cachedTraversalDistanceField = AccessTools.Field(typeof(WorldGrid), "cachedTraversalDistance");
        static FieldInfo cachedTraversalDistanceForStartField = AccessTools.Field(typeof(WorldGrid), "cachedTraversalDistanceForStart");
        static FieldInfo cachedTraversalDistanceForEndField = AccessTools.Field(typeof(WorldGrid), "cachedTraversalDistanceForEnd");

        static bool Prefix(WorldGrid __instance)
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
            cachedTraversalDistanceField.SetValue(grid, -1);
            cachedTraversalDistanceForStartField.SetValue(grid, -1);
            cachedTraversalDistanceForEndField.SetValue(grid, -1);

            copyFrom = null;

            return false;
        }
    }

    public static class WorldRendererCtorPatch
    {
        public static WorldRenderer copyFrom;

        static FieldInfo layersField = AccessTools.Field(typeof(WorldRenderer), "layers");

        static bool Prefix(WorldRenderer __instance)
        {
            if (copyFrom == null) return true;

            layersField.SetValue(__instance, layersField.GetValue(copyFrom));
            copyFrom = null;

            return false;
        }
    }

    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch(nameof(UIRoot.UIRootOnGUI))]
    public static class UIRootPatch
    {
        static bool firstRun = true;

        static void Prefix()
        {
            if (firstRun)
            {
                GUI.skin.font = Text.fontStyles[1].font;
                firstRun = false;
            }
        }
    }

    [HarmonyPatch(typeof(SavedGameLoader))]
    [HarmonyPatch(nameof(SavedGameLoader.LoadGameFromSaveFile))]
    [HarmonyPatch(new Type[] { typeof(string) })]
    public static class LoadPatch
    {
        public static XmlDocument gameToLoad;

        static bool Prefix(string fileName)
        {
            if (fileName != "server") return true;
            if (gameToLoad == null) return false;

            DeepProfiler.Start("InitLoading");

            ScribeUtil.StartLoading(gameToLoad);

            ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);

            DeepProfiler.End();

            Scribe.EnterNode("game");
            Current.Game = new Game();
            Current.Game.InitData = new GameInitData();
            Prefs.PauseOnLoad = false;
            Current.Game.LoadGame(); // calls Scribe.loader.FinalizeLoading()
            Prefs.PauseOnLoad = true;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

            Log.Message("Game loaded");

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Log.Message("Client maps: " + Current.Game.Maps.Count());

                if (!Current.Game.Maps.Any())
                {
                    MemoryUtility.UnloadUnusedUnityAssets();
                    Find.World.renderer.RegenerateAllLayersNow();
                }

                gameToLoad = null;

                /*Find.WindowStack.Add(new CustomSelectLandingSite()
                {
                    nextAct = () => Settle()
                });*/

                if (Multiplayer.client == null || Multiplayer.localServer != null) return;

                //Multiplayer.client.State = new ClientPlayingState(Multiplayer.client);
                //Multiplayer.client.Send(Packets.CLIENT_WORLD_LOADED);

                //Multiplayer.client.Send(Packets.CLIENT_ENCOUNTER_REQUEST, new object[] { Find.WorldObjects.Settlements.First(s => Find.World.GetComponent<MultiplayerWorldComp>().playerFactions.ContainsValue(s.Faction)).Tile });
            });

            return false;
        }

        private static void Settle()
        {
            // notify the server of map gen pause?

            Find.GameInitData.mapSize = 150;
            Find.GameInitData.startingPawns.Add(StartingPawnUtility.NewGeneratedStartingPawn());
            Find.GameInitData.startingPawns.Add(StartingPawnUtility.NewGeneratedStartingPawn());

            FactionBase factionBase = (FactionBase)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.FactionBase);
            factionBase.SetFaction(Multiplayer.RealPlayerFaction);
            factionBase.Tile = Find.GameInitData.startingTile;
            factionBase.Name = FactionBaseNameGenerator.GenerateFactionBaseName(factionBase);
            Find.WorldObjects.Add(factionBase);

            IntVec3 intVec = new IntVec3(Find.GameInitData.mapSize, 1, Find.GameInitData.mapSize);
            Map visibleMap = MapGenerator.GenerateMap(intVec, factionBase, factionBase.MapGeneratorDef, factionBase.ExtraGenStepDefs, null);
            Find.World.info.initialMapSize = intVec;
            PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.NewColonyOptimism);
            Current.Game.VisibleMap = visibleMap;
            Find.CameraDriver.JumpToVisibleMapLoc(MapGenerator.PlayerStartSpot);
            Find.CameraDriver.ResetSize();
            Current.Game.InitData = null;

            Log.Message("New map: " + visibleMap.GetUniqueLoadID());

            ClientPlayingState.SyncClientWorldObj(factionBase);
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot))]
    [HarmonyPatch(nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class MainButtonsPatch
    {
        static bool Prefix()
        {
            Text.Font = GameFont.Small;
            string text = Find.TickManager.TicksGame.ToString() + " " + TickPatch.timerInt + " " + TickPatch.tickUntil;

            if (Find.VisibleMap != null)
            {
                text += " r:" + Find.VisibleMap.reservationManager.AllReservedThings().Count();

                if (Find.VisibleMap.GetComponent<MultiplayerMapComp>().factionHaulables.TryGetValue(Find.VisibleMap.info.parent.Faction.GetUniqueLoadID(), out ListerHaulables haul))
                    text += " h:" + haul.ThingsPotentiallyNeedingHauling().Count;

                if (Find.VisibleMap.GetComponent<MultiplayerMapComp>().factionSlotGroups.TryGetValue(Find.VisibleMap.info.parent.Faction.GetUniqueLoadID(), out SlotGroupManager groups))
                    text += " sg:" + groups.AllGroupsListForReading.Count;

                AsyncTimeMapComp comp = Find.VisibleMap.GetComponent<AsyncTimeMapComp>();
                string text1 = "" + comp.mapTicks;
                Rect rect1 = new Rect(80f, 110f, 330f, Text.CalcHeight(text1, 330f));
                Widgets.Label(rect1, text1);
            }

            Rect rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text, 330f));
            Widgets.Label(rect, text);

            return Find.Maps.Count > 0;
        }
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.TickManagerUpdate))]
    public static class TimeChangePatch
    {
        private static TimeSpeed lastSpeed;

        static void Prefix()
        {
            if (Multiplayer.client != null && Find.TickManager.CurTimeSpeed != lastSpeed)
            {
                Multiplayer.client.SendCommand(CommandType.WORLD_TIME_SPEED, (byte)Find.TickManager.CurTimeSpeed);
                Find.TickManager.CurTimeSpeed = lastSpeed;
                return;
            }

            lastSpeed = Find.TickManager.CurTimeSpeed;
        }

        public static void SetSpeed(TimeSpeed speed)
        {
            Find.TickManager.CurTimeSpeed = speed;
            lastSpeed = speed;
        }
    }

    [HarmonyPatch(typeof(ScribeLoader))]
    [HarmonyPatch(nameof(ScribeLoader.FinalizeLoading))]
    public static class FinalizeLoadingGame
    {
        static void Prefix(ref string __state)
        {
            __state = Scribe.loader.curXmlParent.Name;
        }

        // Called after ResolveAllCrossReferences and right before Map.FinalizeLoading
        static void Postfix(string __state)
        {
            if (Current.ProgramState != ProgramState.MapInitializing || __state != "game") return;

            RegisterCrossRefs();
        }

        static void RegisterCrossRefs()
        {
            foreach (Faction f in Find.FactionManager.AllFactions)
                ScribeUtil.crossRefs.RegisterLoaded(f);

            foreach (Map map in Find.Maps)
                ScribeUtil.crossRefs.RegisterLoaded(map);
        }
    }

    [HarmonyPatch(typeof(CaravanArrivalAction_AttackSettlement))]
    [HarmonyPatch(nameof(CaravanArrivalAction_AttackSettlement.Arrived))]
    public static class AttackSettlementPatch
    {
        static FieldInfo settlementField = typeof(CaravanArrivalAction_AttackSettlement).GetField("settlement", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(CaravanArrivalAction_AttackSettlement __instance, Caravan caravan)
        {
            if (Multiplayer.client == null) return true;

            Settlement settlement = (Settlement)settlementField.GetValue(__instance);
            if (settlement.Faction.def != Multiplayer.factionDef) return true;

            Multiplayer.client.Send(Packets.CLIENT_ENCOUNTER_REQUEST, new object[] { settlement.Tile });

            return false;
        }
    }

    [HarmonyPatch(typeof(Settlement))]
    [HarmonyPatch(nameof(Settlement.ShouldRemoveMapNow))]
    public static class ShouldRemoveMap
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.client != null)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(FactionBaseDefeatUtility))]
    [HarmonyPatch("IsDefeated")]
    public static class IsDefeated
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.client != null)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.StartJob))]
    public static class JobTrackerStart
    {
        public static FieldInfo pawnField = typeof(Pawn_JobTracker).GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic);

        static void Prefix(Pawn_JobTracker __instance, Job newJob, ref Container<Map> __state)
        {
            if (Multiplayer.client == null) return;
            Pawn pawn = (Pawn)pawnField.GetValue(__instance);

            Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " start job " + pawn + " " + newJob);

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
            if (Multiplayer.client == null) return;
            Pawn pawn = (Pawn)JobTrackerStart.pawnField.GetValue(__instance);
            Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " end job " + pawn + " " + __instance.curJob + " " + condition);

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
            if (Multiplayer.client == null) return;
            Pawn pawn = (Pawn)JobTrackerStart.pawnField.GetValue(__instance);

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

    /*[HarmonyPatch(typeof(UIRoot_Play))]
    [HarmonyPatch(nameof(UIRoot_Play.UIRootOnGUI))]
    public static class OnGuiPatch
    {
        static bool Prefix()
        {
            if (OnMainThread.currentLongAction == null) return true;

            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp || Event.current.type == EventType.ScrollWheel)
                return false;

            return true;
        }

        static void Postfix()
        {
            if (OnMainThread.currentLongAction == null) return;

            string text = OnMainThread.GetActionsText();
            Vector2 size = Text.CalcSize(text);
            int width = Math.Max(240, (int)size.x + 40);
            int height = Math.Max(50, (int)size.y + 20);
            Rect rect = new Rect((UI.screenWidth - width) / 2, (UI.screenHeight - height) / 2, width, height);
            rect.Rounded();

            Widgets.DrawShadowAround(rect);
            Widgets.DrawWindowBackground(rect);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }*/

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
    [HarmonyPatch("GetNextID")]
    public static class UniqueIdsPatch
    {
        static void Postfix(ref int __result)
        {
            if (Multiplayer.client == null || Multiplayer.mainBlock == null) return;

            Map map = ThingContext.CurrentMap ?? Multiplayer.currentMap;
            if (map != null)
            {
                IdBlock block = map.GetComponent<MultiplayerMapComp>().encounterIdBlock;
                if (block != null)
                {
                    __result = block.NextId();
                    return;
                }
            }

            __result = Multiplayer.mainBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(GizmoGridDrawer))]
    [HarmonyPatch(nameof(GizmoGridDrawer.DrawGizmoGrid))]
    public static class DrawGizmosPatch
    {
        public static bool drawingGizmos;

        static void Prefix() => drawingGizmos = true;

        static void Postfix() => drawingGizmos = false;
    }

    [HarmonyPatch(typeof(Pawn_DraftController))]
    [HarmonyPatch(nameof(Pawn_DraftController.Drafted), PropertyMethod.Setter)]
    public static class DraftSetPatch
    {
        public static bool dontHandle;

        static bool Prefix(Pawn_DraftController __instance, bool value)
        {
            if (Multiplayer.client == null || dontHandle) return true;
            if (!DrawGizmosPatch.drawingGizmos) return true;

            Multiplayer.client.SendCommand(CommandType.DRAFT, __instance.pawn.Map.GetUniqueLoadID(), __instance.pawn.GetUniqueLoadID(), value);

            return false;
        }
    }

    [HarmonyPatch(typeof(PawnComponentsUtility))]
    [HarmonyPatch(nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    public static class AddAndRemoveCompsPatch
    {
        static void Prefix(Pawn pawn, ref Container<Map> __state)
        {
            if (Multiplayer.client == null || pawn.Faction == null) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Postfix(Pawn pawn, Container<Map> __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(ThingWithComps))]
    [HarmonyPatch(nameof(ThingWithComps.InitializeComps))]
    public static class InitCompsPatch
    {
        private static FieldInfo compsField = typeof(ThingWithComps).GetField("comps", BindingFlags.NonPublic | BindingFlags.Instance);

        static void Postfix(ThingWithComps __instance)
        {
            if (compsField.GetValue(__instance) == null)
                compsField.SetValue(__instance, new List<ThingComp>());

            MultiplayerThingComp comp = new MultiplayerThingComp() { parent = __instance };
            __instance.AllComps.Add(comp);
            comp.Initialize(null);
        }
    }

    [HarmonyPatch(typeof(Plant))]
    [HarmonyPatch(nameof(Plant.GetInspectString))]
    public static class PlantInspect
    {
        static void Postfix(Plant __instance, ref string __result)
        {
            __result += __instance.GetComp<MultiplayerThingComp>().CompInspectStringExtra();
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
                    //Find.WindowStack.Add(new ThinkTreeWindow(__instance));
                    // Log.Message("" + Multiplayer.mainBlock.blockStart);
                    // Log.Message("" + __instance.Map.GetComponent<MultiplayerMapComp>().encounterIdBlock.current);
                    //Log.Message("" + __instance.Map.GetComponent<MultiplayerMapComp>().encounterIdBlock.GetHashCode());

                    __instance.apparel.WornApparel.Sort((Apparel a, Apparel b) => a.def.apparel.LastLayer.CompareTo(b.def.apparel.LastLayer));
                    foreach (var a in __instance.apparel.WornApparel)
                        Log.Message("" + a);
                }
            });
        }
    }

    public class Dialog_String : Dialog_Rename
    {
        private Action<string> action;

        public Dialog_String(Action<string> action)
        {
            this.action = action;
        }

        protected override void SetName(string name)
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
                    if (__instance is Caravan c)
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
                    }

                    Find.WindowStack.Add(new Dialog_JumpTo(i =>
                    {
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
        static bool Prefix() => Multiplayer.client == null || MultiplayerMapComp.tickingFactions;
    }

    [HarmonyPatch(typeof(ResourceCounter))]
    [HarmonyPatch(nameof(ResourceCounter.ResourceCounterTick))]
    public static class ResourcesTickPatch
    {
        static bool Prefix() => Multiplayer.client == null || MultiplayerMapComp.tickingFactions;
    }

    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.WindowsForcePause), PropertyMethod.Getter)]
    public static class WindowsPausePatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.client != null)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.Add))]
    public static class WindowsAddPatch
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.client != null && window is Dialog_RenameZone)
            {
                Messages.Message("Action not available in multiplayer.", MessageTypeDefOf.RejectInput);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(CompForbiddable))]
    [HarmonyPatch(nameof(CompForbiddable.PostSplitOff))]
    public static class ForbiddableSplitPatch
    {
        static bool Prefix(CompForbiddable __instance, Thing piece)
        {
            if (Multiplayer.client == null) return true;
            piece.SetForbidden(__instance.parent.IsForbidden(Faction.OfPlayer));
            return false;
        }
    }

    [HarmonyPatch(typeof(ForbidUtility))]
    [HarmonyPatch(nameof(ForbidUtility.IsForbidden))]
    [HarmonyPatch(new Type[] { typeof(Thing), typeof(Faction) })]
    public static class IsForbiddenPatch
    {
        static void Postfix(Thing t, ref bool __result)
        {
            if (Multiplayer.client == null || Current.ProgramState != ProgramState.Playing) return;

            ThingWithComps thing = t as ThingWithComps;
            if (thing == null) return;

            MultiplayerThingComp comp = thing.GetComp<MultiplayerThingComp>();
            CompForbiddable forbiddable = thing.GetComp<CompForbiddable>();
            if (comp == null || forbiddable == null) return;

            string factionId = Faction.OfPlayer.GetUniqueLoadID();
            if (comp.factionForbidden.TryGetValue(factionId, out bool forbidden))
                __result = forbidden;
            else if (!t.Spawned)
                __result = false;
            else if (factionId == t.Map.ParentFaction.GetUniqueLoadID())
                __result = forbiddable.Forbidden;
            else
                __result = true;
        }
    }

    [HarmonyPatch(typeof(CompForbiddable))]
    [HarmonyPatch(nameof(CompForbiddable.Forbidden), PropertyMethod.Setter)]
    public static class ForbidSetPatch
    {
        private static FieldInfo forbiddenField = typeof(CompForbiddable).GetField("forbiddenInt", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(CompForbiddable __instance, bool value)
        {
            if (Multiplayer.client == null) return true;

            ThingWithComps thing = __instance.parent;
            MultiplayerThingComp comp = thing.GetComp<MultiplayerThingComp>();

            string factionId = Faction.OfPlayer.GetUniqueLoadID();
            if (comp.factionForbidden.TryGetValue(factionId, out bool forbidden) && forbidden == value) return false;

            if (DrawGizmosPatch.drawingGizmos || ProcessDesigInputPatch.processing)
            {
                Multiplayer.client.SendCommand(CommandType.FORBID, thing.Map.GetUniqueLoadID(), thing.GetUniqueLoadID(), Multiplayer.RealPlayerFaction.GetUniqueLoadID(), value);
                return false;
            }

            if (factionId == Multiplayer.RealPlayerFaction.GetUniqueLoadID())
                forbiddenField.SetValue(__instance, value);

            comp.factionForbidden[factionId] = value;

            if (thing.Spawned)
            {
                if (value)
                    thing.Map.listerHaulables.Notify_Forbidden(thing);
                else
                    thing.Map.listerHaulables.Notify_Unforbidden(thing);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(RandomNumberGenerator_BasicHash))]
    [HarmonyPatch(nameof(RandomNumberGenerator_BasicHash.GetInt))]
    public static class RandPatch
    {
        public static int call;
        private static bool dontLog;

        public static string current;

        static void Prefix()
        {
            if (RandPatches.Ignore || dontLog || Multiplayer.client == null) return;
            if (Current.ProgramState != ProgramState.Playing && !Multiplayer.loadingEncounter) return;

            dontLog = true;

            if (ThingContext.Current != null && !(ThingContext.Current is Plant))
            {
                //Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " " + (call++) + " thing rand " + ThingContext.Current + " " + Rand.Int);
            }
            else if (!current.NullOrEmpty())
            {
                //Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " " + (call++) + " rand call " + current + " " + Rand.Int);
            }
            else if (Multiplayer.loadingEncounter)
            {
                //Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " " + (call++) + " rand encounter " + Rand.Int);
            }

            dontLog = false;
        }
    }

    [HarmonyPatch(typeof(Rand))]
    [HarmonyPatch(nameof(Rand.Seed), PropertyMethod.Setter)]
    public static class RandSetSeedPatch
    {
        public static bool dontLog;

        static void Prefix()
        {
            //if (Current.ProgramState == ProgramState.Playing && !ignore)
            //Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " set seed");
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    public static class TakeOrderedJobPatch
    {
        static bool Prefix(Pawn_JobTracker __instance, Job job, JobTag tag)
        {
            if (Multiplayer.client == null) return true;
            if (__instance.curJob != null && __instance.curJob.JobIsSameAs(job)) return false;

            Pawn pawn = (Pawn)JobTrackerStart.pawnField.GetValue(__instance);
            byte[] jobData = ScribeUtil.WriteExposable(job);
            bool shouldQueue = KeyBindingDefOf.QueueOrder.IsDownEvent;

            Multiplayer.client.SendCommand(CommandType.ORDER_JOB, pawn.Map.GetUniqueLoadID(), pawn.GetUniqueLoadID(), jobData, shouldQueue, 0, (byte)tag);

            return false;
        }

        static void Postfix(ref bool __result)
        {
            if (Multiplayer.client == null) return;
            __result = true;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.TryTakeOrderedJobPrioritizedWork))]
    public static class TakeOrderedWorkPatch
    {
        static bool Prefix(Pawn_JobTracker __instance, Job job, WorkGiver giver, IntVec3 cell)
        {
            if (Multiplayer.client == null) return true;
            if (__instance.curJob != null && __instance.curJob.JobIsSameAs(job)) return false;

            Pawn pawn = (Pawn)JobTrackerStart.pawnField.GetValue(__instance);
            byte[] jobData = ScribeUtil.WriteExposable(job);
            bool shouldQueue = KeyBindingDefOf.QueueOrder.IsDownEvent;
            string workGiver = giver.def.defName;

            Multiplayer.client.SendCommand(CommandType.ORDER_JOB, pawn.Map.GetUniqueLoadID(), pawn.GetUniqueLoadID(), jobData, shouldQueue, 1, workGiver, pawn.Map.cellIndices.CellToIndex(cell));

            return false;
        }

        static void Postfix(ref bool __result)
        {
            if (Multiplayer.client == null) return;
            __result = true;
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.Print))]
    public static class ThingPrintPatch
    {
        static bool Prefix(Thing __instance)
        {
            if (Multiplayer.client == null || !(__instance is Blueprint)) return true;
            return __instance.Faction == null || __instance.Faction == Multiplayer.RealPlayerFaction;
        }
    }

    [HarmonyPatch(typeof(AutoBuildRoofAreaSetter))]
    [HarmonyPatch("TryGenerateAreaNow")]
    public static class AutoRoofPatch
    {
        private static bool ignore;
        private static MethodInfo method = typeof(AutoBuildRoofAreaSetter).GetMethod("TryGenerateAreaNow", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(AutoBuildRoofAreaSetter __instance, Room room)
        {
            if (Multiplayer.client == null || ignore) return true;
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

            ignore = true;
            map.PushFaction(faction);
            method.Invoke(__instance, new object[] { room });
            map.PopFaction();
            ignore = false;

            return false;
        }
    }

    [HarmonyPatch(typeof(Zone))]
    [HarmonyPatch(nameof(Zone.Delete))]
    public static class ZoneDeletePatch
    {
        static bool Prefix(Zone __instance)
        {
            if (Multiplayer.client == null || !DrawGizmosPatch.drawingGizmos) return true;
            Multiplayer.client.SendCommand(CommandType.DELETE_ZONE, Multiplayer.RealPlayerFaction.GetUniqueLoadID(), __instance.Map.GetUniqueLoadID(), __instance.label);
            return false;
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.ExposeData))]
    public static class PawnExposeDataPrefix
    {
        public static Container<Map> state;

        // postfix so faction data is already loaded
        static void Postfix(Thing __instance)
        {
            if (!(__instance is Pawn)) return;
            if (Multiplayer.client == null || __instance.Faction == null || Find.FactionManager == null || Find.FactionManager.AllFactions.Count() == 0) return;

            ThingContext.Push(__instance);
            state = __instance.Map;
            __instance.Map.PushFaction(__instance.Faction);
        }
    }

    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch(nameof(Pawn.ExposeData))]
    public static class PawnExposeDataPostfix
    {
        static void Postfix()
        {
            if (PawnExposeDataPrefix.state != null)
            {
                PawnExposeDataPrefix.state.PopFaction();
                ThingContext.Pop();
                PawnExposeDataPrefix.state = null;
            }
        }
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.TickRateMultiplier), PropertyMethod.Getter)]
    public static class TickRatePatch
    {
        static bool Prefix(TickManager __instance, ref float __result)
        {
            if (Multiplayer.client == null) return true;

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

}
