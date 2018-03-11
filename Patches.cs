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

namespace Multiplayer
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

                optList.Insert(0, new ListableOption("Run 1000 ticks", () =>
                {
                    Stopwatch ticksStart = Stopwatch.StartNew();
                    for (int i = 0; i < 1000; i++)
                    {
                        Find.TickManager.DoSingleTick();
                    }
                    Log.Message("1000 ticks took " + ticksStart.ElapsedMilliseconds + "ms (" + (ticksStart.ElapsedMilliseconds / 1000.0) + ")");
                    //ProfilerExperiment.Print();
                }));

                optList.Insert(0, new ListableOption("Reload", () =>
                {
                    LongEventHandler.QueueLongEvent(() =>
                    {
                        ExposableProfiler.timing.Clear();
                        Prefs.PauseOnLoad = false;

                        MapDrawerRegenPatch.remember = Find.VisibleMap.mapDrawer;

                        Stopwatch watch = Stopwatch.StartNew();
                        byte[] wholeGame = ScribeUtil.WriteSingle(Current.Game, "game");
                        Log.Message("saving " + watch.ElapsedMilliseconds);

                        Log.Message("wholegame " + wholeGame.Length);
                        MemoryUtility.ClearAllMapsAndWorld();

                        Prefs.LogVerbose = true;
                        time = Stopwatch.StartNew();
                        Multiplayer.savedWorld = wholeGame;
                        Multiplayer.mapsData = new byte[0];
                        SavedGameLoader.LoadGameFromSaveFile("server");
                        Prefs.PauseOnLoad = true;
                        Prefs.LogVerbose = false;

                        Log.Message("loading " + time.ElapsedMilliseconds);

                        foreach (KeyValuePair<Type, List<double>> p in ExposableProfiler.timing.OrderBy(p => p.Value.Sum()))
                        {
                            Log.Message(p.Key.FullName + " avg: " + p.Value.Average() + " max: " + p.Value.Max() + " min: " + p.Value.Min() + " sum: " + ((double)p.Value.Sum() / Stopwatch.Frequency) + " count: " + p.Value.Count);
                        }

                        Log.Message("exposa " + ExposableProfiler.timing.Sum(p => p.Value.Sum()));
                    }, "Test", false, null);
                }));
            }

            if (Multiplayer.client != null && Multiplayer.server == null)
                optList.RemoveAll(opt => opt.label == "Save".Translate());
        }

        public static void AddHostButton(List<ListableOption> buttons)
        {
            if (Multiplayer.server != null)
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
        public static Stopwatch time;
        public static MapDrawer remember;

        static FieldInfo f = typeof(MapDrawer).GetField("sections", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(MapDrawer __instance)
        {
            RandPatches.Ignore = true;
            Rand.PushState();

            time = Stopwatch.StartNew();

            if (remember == null) return true;

            Section[,] old = (Section[,])f.GetValue(remember);
            foreach (Section s in old)
                s.map = Find.VisibleMap;
            f.SetValue(__instance, old);

            remember = null;

            return false;
        }

        static void Postfix()
        {
            RandPatches.Ignore = false;
            Rand.PopState();

            Log.Message("regenerate took " + time.ElapsedMilliseconds + " game " + MainMenuPatch.time.ElapsedMilliseconds);
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
        static bool Prefix(string fileName)
        {
            if (fileName != "server") return true;
            if (Multiplayer.savedWorld == null) return false;

            DeepProfiler.Start("InitLoading");
            ScribeUtil.StartLoading(Multiplayer.savedWorld);

            if (Multiplayer.mapsData.Length > 0)
            {
                XmlDocument mapsXml = new XmlDocument();
                using (MemoryStream stream = new MemoryStream(Multiplayer.mapsData))
                    mapsXml.Load(stream);

                XmlNode gameNode = Scribe.loader.curXmlParent["game"];
                gameNode.RemoveChildIfPresent("maps");
                gameNode["taleManager"]["tales"].RemoveAll();

                XmlNode newMaps = gameNode.OwnerDocument.ImportNode(mapsXml.DocumentElement["maps"], true);
                gameNode.AppendChild(newMaps);
            }

            ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);

            DeepProfiler.End();

            if (Scribe.EnterNode("game"))
            {
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Prefs.PauseOnLoad = false;
                Current.Game.LoadGame(); // calls Scribe.loader.FinalizeLoading()
                Prefs.PauseOnLoad = true;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            }

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Log.Message("Client maps: " + Current.Game.Maps.Count());

                Multiplayer.savedWorld = null;
                Multiplayer.mapsData = null;

                if (!Current.Game.Maps.Any())
                {
                    MemoryUtility.UnloadUnusedUnityAssets();
                    Find.World.renderer.RegenerateAllLayersNow();
                }

                /*Find.WindowStack.Add(new CustomSelectLandingSite()
                {
                    nextAct = () => Settle()
                });*/

                if (Multiplayer.client == null || Multiplayer.server != null) return;

                Multiplayer.client.SetState(new ClientPlayingState(Multiplayer.client));
                Multiplayer.client.Send(Packets.CLIENT_WORLD_LOADED);

                Multiplayer.client.Send(Packets.CLIENT_ENCOUNTER_REQUEST, new object[] { Find.WorldObjects.Settlements.First(s => Find.World.GetComponent<MultiplayerWorldComp>().playerFactions.ContainsValue(s.Faction)).Tile });
            });

            return false;
        }

        private static void Settle()
        {
            byte[] extra = ScribeUtil.WriteSingle(new LongActionGenerating() { username = Multiplayer.username });
            Multiplayer.client.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { ServerAction.LONG_ACTION_SCHEDULE, extra });

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

            Multiplayer.client.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { ServerAction.LONG_ACTION_END, extra });
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot))]
    [HarmonyPatch(nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class MainButtonsPatch
    {
        static bool Prefix()
        {
            Text.Font = GameFont.Small;
            string text = Find.TickManager.TicksGame.ToString() + " " + TickPatch.timerInt;

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
    public static class TimeChangeCommandPatch
    {
        private static TimeSpeed lastSpeed;

        static void Prefix()
        {
            if (Multiplayer.client != null && Find.TickManager.CurTimeSpeed != lastSpeed)
            {
                Multiplayer.client.SendAction(ServerAction.TIME_SPEED, (byte)Find.TickManager.CurTimeSpeed);
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

    [HarmonyPatch(typeof(GameDataSaveLoader))]
    [HarmonyPatch(nameof(GameDataSaveLoader.SaveGame))]
    public static class SavePatch
    {
        static bool Prefix()
        {
            if (Multiplayer.client == null || Multiplayer.server != null)
                return true;

            ScribeUtil.StartWriting();
            Scribe.EnterNode("savedMaps");
            List<Map> list = Current.Game.Maps.FindAll(map => map.IsPlayerHome);
            Scribe_Collections.Look(ref list, "maps", LookMode.Deep);
            byte[] data = ScribeUtil.FinishWriting();
            Multiplayer.client.Send(Packets.CLIENT_QUIT_MAPS, data);

            return false;
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

        // called after cross refs and right before map finalization
        static void Postfix(string __state)
        {
            if (Current.ProgramState != ProgramState.MapInitializing || __state != "game") return;

            RegisterCrossRefs();

            if (Multiplayer.client == null || Multiplayer.server != null) return;

            FinalizeFactions();
        }

        static void RegisterCrossRefs()
        {
            foreach (Faction f in Find.FactionManager.AllFactions)
                ScribeUtil.crossRefs.RegisterLoaded(f);

            foreach (Map map in Find.Maps)
                ScribeUtil.crossRefs.RegisterLoaded(map);
        }

        static void FinalizeFactions()
        {
            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();

            Faction.OfPlayer.def = Multiplayer.factionDef;
            Faction clientFaction = comp.playerFactions[Multiplayer.username];
            clientFaction.def = FactionDefOf.PlayerColony;

            // todo actually handle relations
            foreach (Faction current in Find.FactionManager.AllFactionsListForReading)
            {
                if (current == clientFaction) continue;
                current.TryMakeInitialRelationsWith(clientFaction);
            }

            Log.Message("Client faction: " + clientFaction.Name + " / " + clientFaction.GetUniqueLoadID());
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
        static void Postfix(ref bool __result) => __result = false;
    }

    [HarmonyPatch(typeof(FactionBaseDefeatUtility))]
    [HarmonyPatch("IsDefeated")]
    public static class IsDefeated
    {
        static void Postfix(ref bool __result) => __result = false;
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

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch("ExposeComponents")]
    public static class MapExposeComps
    {
        static Stopwatch watch;

        static void Prefix()
        {
            watch = Stopwatch.StartNew();
        }

        static void Postfix()
        {
            Log.Message("ExposeComps " + watch.ElapsedMilliseconds);
            watch = null;
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch("BuildCompressedString")]
    public static class BuildCompressedString
    {
        static Stopwatch watch;

        static void Prefix()
        {
            watch = Stopwatch.StartNew();
        }

        static void Postfix()
        {
            Log.Message("BuildCompressedString " + watch.ElapsedMilliseconds);
            watch = null;
        }
    }

    [HarmonyPatch(typeof(CompressibilityDeciderUtility))]
    [HarmonyPatch(nameof(CompressibilityDeciderUtility.IsSaveCompressible))]
    public static class SaveCompressible
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.savingForEncounter)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(UIRoot_Play))]
    [HarmonyPatch(nameof(UIRoot_Play.UIRootOnGUI))]
    public static class OnGuiPatch
    {
        static bool Prefix()
        {
            if (OnMainThread.currentLongAction == null) return true;
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp || Event.current.type == EventType.ScrollWheel) return false;
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
    [HarmonyPatch("GetNextID")]
    public static class UniqueIdsPatch
    {
        static void Postfix(ref int __result)
        {
            if (Multiplayer.mainBlock == null) return;
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

            Multiplayer.client.SendAction(ServerAction.DRAFT, __instance.pawn.Map.GetUniqueLoadID(), __instance.pawn.GetUniqueLoadID(), value);

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

    [HarmonyPatch(typeof(UniqueIDsManager))]
    [HarmonyPatch(nameof(UniqueIDsManager.GetNextThingID))]
    public static class GetNextThingIdPatch
    {
        static void Postfix(ref int __result)
        {
            Map map = ThingContext.CurrentMap ?? Multiplayer.currentMap;
            if (map == null) return;

            IdBlock block = map.GetComponent<MultiplayerMapComp>().encounterIdBlock;
            if (block != null)
            {
                __result = block.NextId();
                Log.Message(Find.TickManager.TicksGame + " " + Multiplayer.username + " new thing " + map + " " + __result);
            }
        }
    }

    [HarmonyPatch(typeof(UniqueIDsManager))]
    [HarmonyPatch(nameof(UniqueIDsManager.GetNextJobID))]
    public static class GetNextJobIdPatch
    {
        static void Postfix(ref int __result)
        {
            Map map = ThingContext.CurrentMap ?? Multiplayer.currentMap;
            if (map == null) return;

            IdBlock block = map.GetComponent<MultiplayerMapComp>().encounterIdBlock;
            if (block != null)
                __result = block.NextId();
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
                Multiplayer.client.SendAction(ServerAction.FORBID, thing.Map.GetUniqueLoadID(), thing.GetUniqueLoadID(), Multiplayer.RealPlayerFaction.GetUniqueLoadID(), value);
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
            byte[] jobData = ScribeUtil.WriteSingle(job);
            bool shouldQueue = KeyBindingDefOf.QueueOrder.IsDownEvent;

            Multiplayer.client.SendAction(ServerAction.ORDER_JOB, pawn.Map.GetUniqueLoadID(), pawn.GetUniqueLoadID(), jobData, shouldQueue, 0, (byte)tag);

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
            byte[] jobData = ScribeUtil.WriteSingle(job);
            bool shouldQueue = KeyBindingDefOf.QueueOrder.IsDownEvent;
            string workGiver = giver.def.defName;

            Multiplayer.client.SendAction(ServerAction.ORDER_JOB, pawn.Map.GetUniqueLoadID(), pawn.GetUniqueLoadID(), jobData, shouldQueue, 1, workGiver, pawn.Map.cellIndices.CellToIndex(cell));

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
            Multiplayer.client.SendAction(ServerAction.DELETE_ZONE, Multiplayer.RealPlayerFaction.GetUniqueLoadID(), __instance.Map.GetUniqueLoadID(), __instance.label);
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

    [HarmonyPatch(typeof(Pawn_ApparelTracker))]
    [HarmonyPatch("SortWornApparelIntoDrawOrder")]
    public static class ApparelPatch
    {
        static void Postfix(Pawn_ApparelTracker __instance)
        {
            MpLog.Log("apparel sort " + __instance.pawn);
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
