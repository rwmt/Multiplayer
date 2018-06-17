using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;
using Multiplayer.Common;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(PatchProcessor), nameof(PatchProcessor.Patch))]
    static class PatchProcessorPatch
    {
        static void Prefix(List<MethodBase> ___originals)
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
    }

    [HarmonyPatch(typeof(MainMenuDrawer))]
    [HarmonyPatch(nameof(MainMenuDrawer.DoMainMenuControls))]
    public static class MainMenuMarker
    {
        public static bool drawing;

        static void Prefix() => drawing = true;

        static void Postfix() => drawing = false;
    }

    [HarmonyPatch(typeof(WildSpawner))]
    [HarmonyPatch(nameof(WildSpawner.WildSpawnerTick))]
    public static class WildSpawnerTickMarker
    {
        public static bool ticking;

        static void Prefix() => ticking = true;

        static void Postfix() => ticking = false;
    }

    [HarmonyPatch(typeof(SteadyAtmosphereEffects))]
    [HarmonyPatch(nameof(SteadyAtmosphereEffects.SteadyAtmosphereEffectsTick))]
    public static class SteadyAtmosphereEffectsTickkMarker
    {
        public static bool ticking;

        static void Prefix() => ticking = true;

        static void Postfix() => ticking = false;
    }

    [HarmonyPatch(typeof(OptionListingUtility))]
    [HarmonyPatch(nameof(OptionListingUtility.DrawOptionListing))]
    public static class MainMenuPatch
    {
        public static Stopwatch time = new Stopwatch();

        static void Prefix(Rect rect, List<ListableOption> optList)
        {
            if (!MainMenuMarker.drawing) return;

            if (Current.ProgramState == ProgramState.Entry)
            {
                int newColony = optList.FindIndex(opt => opt.label == "NewColony".Translate());
                if (newColony != -1)
                    optList.Insert(newColony + 1, new ListableOption("Connect to server", () =>
                    {
                        Find.WindowStack.Add(new ConnectWindow());
                    }));
            }

            if (optList.Any(opt => opt.label == "ReviewScenario".Translate()))
            {
                if (Multiplayer.localServer == null && Multiplayer.client == null)
                {
                    optList.Insert(0, new ListableOption("Host a server", () =>
                    {
                        Find.WindowStack.Add(new HostWindow());
                    }));
                }

                if (Multiplayer.client != null)
                {
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

                        //Multiplayer.localServer.DoAutosave();
                    }));

                    optList.RemoveAll(opt => opt.label == "Save".Translate() || opt.label == "LoadGame".Translate());

                    optList.FirstOrDefault(opt => opt.label == "QuitToMainMenu".Translate()).action = () =>
                    {
                        OnMainThread.StopMultiplayer();
                        GenScene.GoToMainMenu();
                    };

                    optList.FirstOrDefault(opt => opt.label == "QuitToOS".Translate()).action = () => Root.Shutdown();
                }
            }
        }
    }

    // Currently broken
    public static class MapDrawerRegenPatch
    {
        public static Dictionary<string, MapDrawer> copyFrom = new Dictionary<string, MapDrawer>();

        static bool Prefix(MapDrawer __instance)
        {
            Map map = __instance.map;
            if (!copyFrom.TryGetValue(map.GetUniqueLoadID(), out MapDrawer oldDrawer)) return true;

            Section[,] oldSections = oldDrawer.sections;
            foreach (Section s in oldSections)
                s.map = map;
            __instance.sections = oldSections;

            copyFrom.Remove(map.GetUniqueLoadID());

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

    public class FactionEquality : IEqualityComparer<Faction>
    {
        public bool Equals(Faction x, Faction y) => object.Equals(x, y);
        public int GetHashCode(Faction obj) => obj.loadID;
    }

    public class ThingCompEquality : IEqualityComparer<ThingComp>
    {
        public bool Equals(ThingComp x, ThingComp y) => object.Equals(x, y);
        public int GetHashCode(ThingComp obj) => obj.parent.thingIDNumber.Combine(obj.GetType().FullName.GetHashCode());
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

    [HarmonyPatch(typeof(SavedGameLoader))]
    [HarmonyPatch(nameof(SavedGameLoader.LoadGameFromSaveFile))]
    [HarmonyPatch(new[] { typeof(string) })]
    public static class LoadPatch
    {
        public static XmlDocument gameToLoad;

        static bool Prefix()
        {
            if (gameToLoad == null) return false;

            ScribeUtil.StartLoading(gameToLoad);

            Multiplayer.loadingEncounter = true;
            ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);
            Scribe.EnterNode("game");
            Current.Game = new Game();
            Current.Game.InitData = new GameInitData();
            Prefs.PauseOnLoad = false;
            Current.Game.LoadGame(); // calls Scribe.loader.FinalizeLoading()
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Multiplayer.loadingEncounter = false;
            gameToLoad = null;

            Log.Message("Game loaded");

            LongEventHandler.ExecuteWhenFinished(() =>
            {
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

    [HarmonyPatch(typeof(PawnObserver))]
    [HarmonyPatch(nameof(PawnObserver.ObserveSurroundingThings))]
    public static class ObservePatch
    {
        static int i;

        static void Prefix()
        {
            if (Multiplayer.client == null) return;
            MpLog.Log("observer " + ThingContext.Current + " " + i);
            i++;
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot))]
    [HarmonyPatch(nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class MainButtonsPatch
    {
        static bool Prefix()
        {
            Text.Font = GameFont.Small;
            string text = Find.TickManager.TicksGame + " " + TickPatch.timerInt + " " + TickPatch.tickUntil + " " + (TickPatch.tickUntil - (int)TickPatch.timerInt);
            Rect rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text, 330f));
            Widgets.Label(rect, text);

            if (Find.VisibleMap != null)
            {
                MapAsyncTimeComp comp = Find.VisibleMap.GetComponent<MapAsyncTimeComp>();
                string text1 = "" + comp.mapTicks;

                text1 += " r:" + Find.VisibleMap.reservationManager.AllReservedThings().Count();

                int faction = Find.VisibleMap.info.parent.Faction.loadID;
                FactionMapData data = Find.VisibleMap.GetComponent<MultiplayerMapComp>().factionMapData.GetValueSafe(faction);

                if (data != null)
                {
                    text1 += " h:" + data.listerHaulables.ThingsPotentiallyNeedingHauling().Count;
                    text1 += " sg:" + data.slotGroupManager.AllGroupsListForReading.Count;
                }

                Rect rect1 = new Rect(80f, 110f, 330f, Text.CalcHeight(text1, 330f));
                Widgets.Label(rect1, text1);
            }

            if (Widgets.ButtonText(new Rect(Screen.width - 60f, 10f, 50f, 25f), "Chat"))
                Find.WindowStack.Add(Multiplayer.chat);

            if (Widgets.ButtonText(new Rect(Screen.width - 60f, 35f, 50f, 25f), "Packets"))
                Find.WindowStack.Add(Multiplayer.packetLog);

            return Find.Maps.Count > 0;
        }
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.TickManagerUpdate))]
    public static class WorldTimeChangePatch
    {
        private static TimeSpeed lastSpeed = TimeSpeed.Paused;

        static void Prefix()
        {
            if (Multiplayer.client == null || !WorldRendererUtility.WorldRenderedNow) return;

            if (Find.TickManager.CurTimeSpeed != lastSpeed)
            {
                Multiplayer.client.SendCommand(CommandType.WORLD_TIME_SPEED, ScheduledCommand.GLOBAL, (byte)Find.TickManager.CurTimeSpeed);
                Find.TickManager.CurTimeSpeed = lastSpeed;
            }
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
        static bool Prefix(CaravanArrivalAction_AttackSettlement __instance, Caravan caravan)
        {
            if (Multiplayer.client == null) return true;

            Settlement settlement = __instance.settlement;
            if (settlement.Faction.def != Multiplayer.factionDef) return true;

            Multiplayer.client.Send(Packets.CLIENT_ENCOUNTER_REQUEST, new object[] { settlement.Tile });

            return false;
        }
    }

    [HarmonyPatch(typeof(Settlement))]
    [HarmonyPatch(nameof(Settlement.ShouldRemoveMapNow))]
    public static class ShouldRemoveMapPatch
    {
        static bool Prefix() => Multiplayer.client == null;
    }

    [HarmonyPatch(typeof(FactionBaseDefeatUtility))]
    [HarmonyPatch(nameof(FactionBaseDefeatUtility.CheckDefeated))]
    public static class CheckDefeatedPatch
    {
        static bool Prefix() => Multiplayer.client == null;
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

            //Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " start job " + pawn + " " + newJob);

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

            //Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " end job " + pawn + " " + __instance.curJob + " " + condition);

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

    [HarmonyPatch(typeof(Faction))]
    [HarmonyPatch(nameof(Faction.OfPlayer), PropertyMethod.Getter)]
    public static class FactionOfPlayerPatch
    {
        static void Prefix()
        {
            if (Multiplayer.Ticking && FactionContext.stack.Count == 0)
                Log.Warning("Faction context not set during ticking");
        }
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

        static void Postfix(ref int __result)
        {
            if (Multiplayer.client == null) return;

            if (CurrentBlock == null)
            {
                //__result = -1;
                Log.Warning("Tried to get a unique id without an id block set!");
                return;
            }

            __result = CurrentBlock.NextId();
            MpLog.Log("got new id " + __result);

            if (currentBlock.current > currentBlock.blockSize * 0.95f && !currentBlock.overflowHandled)
            {
                Multiplayer.client.Send(Packets.CLIENT_ID_BLOCK_REQUEST, CurrentBlock.mapId);
                currentBlock.overflowHandled = true;
            }
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

                    /*Find.WindowStack.Add(new Dialog_JumpTo(i =>
                    {
                        Find.WorldCameraDriver.JumpTo(i);
                        Find.WorldSelector.selectedTile = i;
                    }));*/
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
            // Zone names need to be unique and constant for identification
            if (Multiplayer.client != null && window is Dialog_RenameZone)
            {
                Messages.Message("Action not available in multiplayer.", MessageTypeDefOf.RejectInput);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Plant))]
    [HarmonyPatch(nameof(Plant.TickLong))]
    public static class PlantTickLong
    {
        static void Prefix()
        {
            Rand.PushState();
        }

        static void Postfix()
        {
            Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(Projectile))]
    [HarmonyPatch(nameof(Projectile.Tick))]
    public static class ProjectileTick
    {
        static void Postfix(Projectile __instance)
        {
            MpLog.Log("projectile " + __instance.ticksToImpact + " " + __instance.ExactPosition);
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

            if (MapAsyncTimeComp.tickingMap != null && false)
            {
                call++;

                if (ThingContext.Current == null || !(ThingContext.Current is Plant || ThingContext.Current.def == ThingDefOf.SteamGeyser))
                    if (!(WildSpawnerTickMarker.ticking || SteadyAtmosphereEffectsTickkMarker.ticking) || (Find.TickManager.TicksGame > 9670 && Find.TickManager.TicksGame < 9690))
                        MpLog.Log(call + " thing rand " + ThingContext.Current + " " + Rand.Int);
            }

            if (ThingContext.Current != null && !(ThingContext.Current is Plant) && !(ThingContext.Current.def == ThingDefOf.SteamGeyser))
            {
                //MpLog.Log((call++) + " thing rand " + ThingContext.Current + " " + Rand.Int);
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
            if (dontLog) return;
            //if (MapAsyncTimeComp.tickingMap != null)
            //MpLog.Log("set seed");
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
    [HarmonyPatch(nameof(AutoBuildRoofAreaSetter.TryGenerateAreaNow))]
    public static class AutoRoofPatch
    {
        static bool Prefix(AutoBuildRoofAreaSetter __instance, Room room, ref Map __state)
        {
            if (Multiplayer.client == null) return true;
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
        static void Postfix(Pawn_DrawTracker __instance, ref Vector3 __result)
        {
            if (Multiplayer.client == null || Multiplayer.ShouldSync) return;

            Vector3 result = __result;
            result -= __instance.tweener.TweenedPos;
            result += __instance.tweener.TweenedPosRoot();
            __result = result;
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

    [HarmonyPatch(typeof(Log))]
    [HarmonyPatch(nameof(Log.Warning))]
    public static class CrossRefWarningPatch
    {
        private static Regex regex = new Regex(@"^Could not resolve reference to object with loadID ([\w.-]*) of type ([\w.<>+]*)\. Was it compressed away");
        public static bool ignore;

        // The only non-generic entry point during cross reference resolving
        static bool Prefix(string text)
        {
            if (Multiplayer.client == null || ignore) return true;

            ignore = true;

            GroupCollection groups = regex.Match(text).Groups;
            if (groups.Count == 3)
            {
                string loadId = groups[1].Value;
                string typeName = groups[2].Value;

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

    [HarmonyPatch(typeof(ITab))]
    [HarmonyPatch(nameof(ITab.SelThing), PropertyMethod.Getter)]
    public static class ITabSelThingPatch
    {
        public static Thing result;

        static void Postfix(ref Thing __result)
        {
            if (result != null)
                __result = result;
        }
    }

    // For instance methods the first parameter is the instance
    // The rest are original method's parameters in order
    [AttributeUsage(AttributeTargets.Method)]
    public class IndexedPatchParameters : Attribute
    {
    }

    [HarmonyPatch(typeof(MethodPatcher))]
    [HarmonyPatch("EmitCallParameter")]
    public static class ParameterNamePatch
    {
        static readonly FieldInfo paramName = AccessTools.Field(typeof(ParameterInfo), "NameImpl");

        static void Prefix(MethodBase original, MethodInfo patch)
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

    // Fix window control focus
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

}