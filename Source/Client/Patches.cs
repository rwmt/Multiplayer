using Harmony;
using Multiplayer.Common;
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
    [HarmonyPatch(typeof(Log))]
    [HarmonyPatch(nameof(Log.ReachedMaxMessagesLimit), MethodType.Getter)]
    static class LogMaxMessagesPatch
    {
        static void Postfix(ref bool __result)
        {
            //if (Multiplayer.Client != null)
            __result = false;
        }
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
                    optList.Insert(newColony + 1, new ListableOption("Multiplayer", () =>
                    {
                        Find.WindowStack.Add(new ServerBrowser());
                    }));
                }
            }

            if (optList.Any(opt => opt.label == "ReviewScenario".Translate()))
            {
                if (Multiplayer.session == null)
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
                        Find.WindowStack.Add(new Dialog_SaveReplay());
                    }));

                    optList.RemoveAll(opt => opt.label == "Save".Translate() || opt.label == "LoadGame".Translate());

                    var quitMenuLabel = "QuitToMainMenu".Translate();
                    var saveAndQuitMenu = "SaveAndQuitToMainMenu".Translate();
                    var quitMenu = optList.Find(opt => opt.label == quitMenuLabel || opt.label == saveAndQuitMenu);

                    if (quitMenu != null)
                    {
                        quitMenu.label = quitMenuLabel;
                        quitMenu.action = () =>
                        {
                            Action action = () =>
                            {
                                OnMainThread.StopMultiplayer();
                                GenScene.GoToMainMenu();
                            };

                            if (Multiplayer.LocalServer != null)
                                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("MpServerCloseConfirmation".Translate(), action, true));
                            else
                                action();
                        };
                    }

                    var quitOSLabel = "QuitToOS".Translate();
                    var saveAndQuitOSLabel = "SaveAndQuitToOS".Translate();
                    var quitOS = optList.Find(opt => opt.label == quitOSLabel || opt.label == saveAndQuitOSLabel);

                    if (quitOS != null)
                    {
                        quitOS.label = quitOSLabel;
                        quitOS.action = () =>
                        {
                            if (Multiplayer.LocalServer != null)
                                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("MpServerCloseConfirmation".Translate(), () => Root.Shutdown(), true));
                            else
                                Root.Shutdown();
                        };
                    }
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

    [HarmonyPatch(typeof(WorldGrid), MethodType.Constructor)]
    public static class WorldGridCachePatch
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

    [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
    public static class WorldRendererCachePatch
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

    [HarmonyPatch(typeof(SavedGameLoaderNow))]
    [HarmonyPatch(nameof(SavedGameLoaderNow.LoadGameFromSaveFileNow))]
    [HarmonyPatch(new[] { typeof(string) })]
    public static class LoadPatch
    {
        public static XmlDocument gameToLoad;

        static bool Prefix()
        {
            if (gameToLoad == null) return true;

            bool prevCompress = SaveCompression.doSaveCompression;
            //SaveCompression.doSaveCompression = true;

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
                foreach (ITickable tickable in TickPatch.AllTickables.Where(t => !(t is ConstantTicker)))
                    tickable.Tick();

                if (!Current.Game.Maps.Any())
                {
                    MemoryUtility.UnloadUnusedUnityAssets();
                    Find.World.renderer.RegenerateAllLayersNow();
                }
            });

            return false;
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI))]
    [HotSwappable]
    public static class MainButtonsPatch
    {
        static bool Prefix()
        {
            Text.Font = GameFont.Small;

            DoDebugInfo();

            if (Multiplayer.IsReplay || TickPatch.skipTo >= 0)
            {
                DrawTimeline();
                DrawSkippingWindow();
            }

            DoButtons();

            return Find.Maps.Count > 0;
        }

        static void DoDebugInfo()
        {
            if (MpVersion.IsDebug && Multiplayer.Client != null)
            {
                int timerLag = (TickPatch.tickUntil - TickPatch.Timer);
                string text = $"{Find.TickManager.TicksGame} {TickPatch.Timer} {TickPatch.tickUntil} {timerLag} {Time.deltaTime * 60f}";
                Rect rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text, 330f));
                Widgets.Label(rect, text);
            }

            if (MpVersion.IsDebug && Multiplayer.Client != null && Find.CurrentMap != null)
            {
                var async = Find.CurrentMap.GetComponent<MapAsyncTimeComp>();
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
                text.Append($"\n{((uint)async.randState)} {(uint)(async.randState >> 32)}");
                text.Append($"\n{(uint)Multiplayer.WorldComp.randState} {(uint)(Multiplayer.WorldComp.randState >> 32)}");
                text.Append($"\n{async.cmds.Count} {Multiplayer.WorldComp.cmds.Count} {async.slower.ForcedNormalSpeed}");

                Rect rect1 = new Rect(80f, 110f, 330f, Text.CalcHeight(text.ToString(), 330f));
                Widgets.Label(rect1, text.ToString());
            }
        }

        static void DoButtons()
        {
            float y = 10f;
            const float btnWidth = 60f;

            if (Multiplayer.session != null && !Multiplayer.IsReplay)
            {
                var btnRect = new Rect(UI.screenWidth - btnWidth - 10f, y, btnWidth, 25f);

                if (Widgets.ButtonText(btnRect, $"Chat{(Multiplayer.session.hasUnread ? "*" : "")}"))
                    Find.WindowStack.Add(new ChatWindow());

                if (TickPatch.skipTo < 0)
                {
                    IndicatorInfo(out Color color, out string text);

                    var indRect = new Rect(btnRect.x - 25f - 5f + 6f / 2f, btnRect.y + 6f / 2f, 19f, 19f);
                    Widgets.DrawRectFast(new Rect(btnRect.x - 25f - 5f + 2f / 2f, btnRect.y + 2f / 2f, 23f, 23f), new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f));
                    Widgets.DrawRectFast(indRect, color);
                    TooltipHandler.TipRegion(indRect, new TipSignal(text, 31641624));
                }

                y += 25f;
            }

            if (MpVersion.IsDebug && Multiplayer.PacketLog != null)
            {
                if (Widgets.ButtonText(new Rect(UI.screenWidth - btnWidth - 10f, y, btnWidth, 25f), "Packets"))
                    Find.WindowStack.Add(Multiplayer.PacketLog);
                y += 25f;
            }

            if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any())
            {
                if (Widgets.ButtonText(new Rect(UI.screenWidth - btnWidth - 10f, y, btnWidth, 25f), "Trading"))
                    Find.WindowStack.Add(new TradingWindow());
                y += 25f;
            }
        }

        static void IndicatorInfo(out Color color, out string text)
        {
            int behind = TickPatch.tickUntil - TickPatch.Timer;
            text = $"You are {behind} ticks behind.";

            if (behind > 30)
            {
                color = new Color(0.9f, 0, 0);
                text += "\n\nConsider lowering the game speed.";
            }
            else if (behind > 15)
            {
                color = Color.yellow;
            }
            else
            {
                color = new Color(0.0f, 0.8f, 0.0f);
            }
        }

        const float TimelineMargin = 50f;
        const float TimelineHeight = 35f;

        static void DrawTimeline()
        {
            Rect rect = new Rect(TimelineMargin, UI.screenHeight - 35f - TimelineHeight - 10f - 30f, UI.screenWidth - TimelineMargin * 2, TimelineHeight + 30f);
            Find.WindowStack.ImmediateWindow(TimelineWindowId, rect, WindowLayer.SubSuper, DrawTimelineWindow, doBackground: false, shadowAlpha: 0);
        }

        static void DrawTimelineWindow()
        {
            Rect rect = new Rect(0, 30f, UI.screenWidth - TimelineMargin * 2, TimelineHeight);

            Widgets.DrawBoxSolid(rect, new Color(0.6f, 0.6f, 0.6f, 0.8f));

            int timerStart = Multiplayer.session.replayTimerStart >= 0 ? Multiplayer.session.replayTimerStart : OnMainThread.cachedAtTime;
            int timerEnd = Multiplayer.session.replayTimerEnd >= 0 ? Multiplayer.session.replayTimerEnd : TickPatch.tickUntil;
            int timeLen = timerEnd - timerStart;

            Widgets.DrawLine(new Vector2(rect.xMin + 2f, rect.yMin), new Vector2(rect.xMin + 2f, rect.yMax), Color.white, 4f);
            Widgets.DrawLine(new Vector2(rect.xMax - 2f, rect.yMin), new Vector2(rect.xMax - 2f, rect.yMax), Color.white, 4f);

            float progress = (TickPatch.Timer - timerStart) / (float)timeLen;
            float progressX = rect.xMin + progress * rect.width;
            Widgets.DrawLine(new Vector2(progressX, rect.yMin), new Vector2(progressX, rect.yMax), Color.green, 7f);

            float mouseX = Event.current.mousePosition.x;
            ReplayEvent mouseEvent = null;

            foreach (var ev in Multiplayer.session.events)
            {
                if (ev.time < timerStart || ev.time > timerEnd)
                    continue;

                var pointX = rect.xMin + (ev.time - timerStart) / (float)timeLen * rect.width;

                //GUI.DrawTexture(new Rect(pointX - 12f, rect.yMin - 24f, 24f, 24f), texture);
                Widgets.DrawLine(new Vector2(pointX, rect.yMin), new Vector2(pointX, rect.yMax), ev.color, 5f);

                if (Mouse.IsOver(rect) && Math.Abs(mouseX - pointX) < 10)
                {
                    mouseX = pointX;
                    mouseEvent = ev;
                }
            }

            if (Mouse.IsOver(rect))
            {
                float mouseProgress = (mouseX - rect.xMin) / rect.width;
                int mouseTimer = timerStart + (int)(timeLen * mouseProgress);

                Widgets.DrawLine(new Vector2(mouseX, rect.yMin), new Vector2(mouseX, rect.yMax), Color.blue, 3f);

                if (Event.current.type == EventType.MouseUp)
                {
                    TickPatch.skipTo = mouseTimer;

                    if (mouseTimer < TickPatch.Timer)
                    {
                        ClientJoiningState.ReloadGame(OnMainThread.cachedMapData.Keys.ToList(), false);
                    }
                }

                if (Event.current.isMouse)
                    Event.current.Use();

                string tooltip = $"Tick {mouseTimer}";
                if (mouseEvent != null)
                    tooltip = $"{mouseEvent.name}\n{tooltip}";

                TooltipHandler.TipRegion(rect, new TipSignal(tooltip, 215462143));
                // No delay between the mouseover and showing
                if (TooltipHandler.activeTips.TryGetValue(215462143, out ActiveTip tip))
                    tip.firstTriggerTime = 0;
            }

            if (TickPatch.skipTo >= 0)
            {
                float pct = (TickPatch.skipTo - timerStart) / (float)timeLen;
                float skipToX = rect.xMin + rect.width * pct;
                Widgets.DrawLine(new Vector2(skipToX, rect.yMin), new Vector2(skipToX, rect.yMax), Color.yellow, 4f);
            }
        }

        public const int SkippingWindowId = 26461263;
        public const int TimelineWindowId = 5723681;

        static void DrawSkippingWindow()
        {
            if (TickPatch.skipTo < 0) return;

            string text = $"Simulating{MpUtil.FixedEllipsis()}";
            float textWidth = Text.CalcSize(text).x;
            float windowWidth = Math.Max(240f, textWidth + 40f);
            Rect rect = new Rect(0, 0, windowWidth, 75f).CenterOn(new Rect(0, 0, UI.screenWidth, UI.screenHeight));

            if (Multiplayer.IsReplay && Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Escape)
            {
                TickPatch.ClearSkipping();
                Event.current.Use();
            }

            Find.WindowStack.ImmediateWindow(SkippingWindowId, rect, WindowLayer.Super, () =>
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                Widgets.Label(rect.AtZero(), text);
                Text.Anchor = TextAnchor.UpperLeft;
            }, absorbInputAroundWindow: true);
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
        static void Prefix(Pawn_JobTracker __instance, Job newJob, ref Container<Map>? __state)
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

        static void Postfix(Container<Map>? __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class JobTrackerEndCurrent
    {
        static void Prefix(Pawn_JobTracker __instance, JobCondition condition, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;
            Pawn pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Postfix(Container<Map>? __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.CheckForJobOverride))]
    public static class JobTrackerOverride
    {
        static void Prefix(Pawn_JobTracker __instance, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;
            Pawn pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            ThingContext.Push(pawn);
            __state = pawn.Map;
        }

        static void Postfix(Container<Map>? __state)
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

        static bool Prefix()
        {
            return Multiplayer.Client == null || !Multiplayer.ShouldSync;
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
        static void Prefix(Pawn pawn, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null || pawn.Faction == null) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Postfix(Pawn pawn, Container<Map>? __state)
        {
            if (__state != null)
                __state.PopFaction();
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
    [HarmonyPatch(new[] { typeof(Bill_Production), typeof(IntVec3) })]
    public static class DialogPatch
    {
        static void Postfix(Dialog_BillConfig __instance)
        {
            __instance.absorbInputAroundWindow = false;
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

    [HarmonyPatch(typeof(PawnTweener))]
    [HarmonyPatch(nameof(PawnTweener.TweenedPos), MethodType.Getter)]
    static class DrawPosPatch
    {
        // Give the root position during ticking
        static void Postfix(PawnTweener __instance, ref Vector3 __result)
        {
            if (Multiplayer.Client == null || Multiplayer.ShouldSync) return;
            __result = __instance.TweenedPosRoot();
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.ExposeData))]
    public static class PawnExposeDataFirst
    {
        public static Container<Map>? state;

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
    [HarmonyPatch(nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
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
    [HarmonyPatch(nameof(KeyBindingDef.IsDownEvent), MethodType.Getter)]
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
                __result = "multiplayer1";
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
            if (Multiplayer.Client == null || ignore) return;

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
            if (Multiplayer.Client == null || ignore) return;

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
            if (Multiplayer.Client == null || ignore) return;

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
    static class LoadGameMarker
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

            if (Scribe.mode == LoadSaveMode.LoadingVars)
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
    [MpPatch(typeof(TutorUtility), nameof(TutorUtility.DoModalDialogIfNotKnown))]
    static class CancelFeedbackNotTargetedAtMe
    {
        public static bool Cancel =>
            Multiplayer.Client != null &&
            Multiplayer.ExecutingCmds &&
            !TickPatch.currentExecutingCmdIssuedBySelf;

        static bool Prefix() => !Cancel;
    }

    [MpPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote), new[] { typeof(IntVec3), typeof(Map), typeof(ThingDef), typeof(float) })]
    [MpPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote), new[] { typeof(Vector3), typeof(Map), typeof(ThingDef), typeof(float) })]
    static class CancelMotesNotTargetedAtMe
    {
        static bool Prefix(ThingDef moteDef)
        {
            if (moteDef == ThingDefOf.Mote_FeedbackGoto)
                return true;

            return !CancelFeedbackNotTargetedAtMe.Cancel;
        }
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

    [HarmonyPatch(typeof(TutorSystem))]
    [HarmonyPatch(nameof(TutorSystem.AdaptiveTrainingEnabled), MethodType.Getter)]
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
                int index = Mathf.Abs(Rand.random.GetInt(iters--) % (i + 1));
                T value = list[index];
                list[index] = list[i];
                list[i] = value;
                i++;
            }
        }
    }

    [HarmonyPatch(typeof(GlowGrid), MethodType.Constructor, new[] { typeof(Map) })]
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
            Log.Message("Rand " + Rand.StateCompressed);
        }

        public static void SetupMap(Map map)
        {
            Log.Message("New map " + map.uniqueID);
            Log.Message("Uniq ids " + Multiplayer.GlobalIdBlock.current);
            Log.Message("Rand " + Rand.StateCompressed);

            MapAsyncTimeComp async = map.AsyncTime();
            MultiplayerMapComp mapComp = map.MpComp();
            Faction dummyFaction = Multiplayer.DummyFaction;

            mapComp.factionMapData[Faction.OfPlayer.loadID] = FactionMapData.FromMap(map, Faction.OfPlayer.loadID);

            mapComp.factionMapData[dummyFaction.loadID] = FactionMapData.New(dummyFaction.loadID, map);
            mapComp.factionMapData[dummyFaction.loadID].areaManager.AddStartingAreas();

            async.mapTicks = Find.Maps.Select(m => m.AsyncTime().mapTicks).Max();
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

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class DisableCaravanSplit
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.Client == null) return true;

            if (window is Dialog_Negotiation)
                return false;

            if (window is Dialog_SplitCaravan)
            {
                Messages.Message("Not available in multiplayer.", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true;
        }
    }

    [MpPatch(typeof(IncidentWorker_CaravanMeeting), nameof(IncidentWorker_CaravanMeeting.CanFireNowSub))]
    [MpPatch(typeof(IncidentWorker_CaravanDemand), nameof(IncidentWorker_CaravanDemand.CanFireNowSub))]
    [MpPatch(typeof(IncidentWorker_RansomDemand), nameof(IncidentWorker_RansomDemand.CanFireNowSub))]
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

    [HarmonyPatch(typeof(Targeter), nameof(Targeter.TargeterOnGUI))]
    [HotSwappable]
    static class DrawPlayerCursors
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || !MultiplayerMod.settings.showCursors || TickPatch.skipTo >= 0) return;

            var curMap = Find.CurrentMap.Index;

            foreach (var player in Multiplayer.session.players)
            {
                if (player.username == Multiplayer.username) continue;
                if (player.map != curMap) continue;

                GUI.color = new Color(1, 1, 1, 0.5f);
                var pos = Vector3.Lerp(player.lastCursor, player.cursor, (float)(Multiplayer.Clock.ElapsedMillisDouble() - player.updatedAt) / 50f).MapToUIPosition();

                var icon = Multiplayer.icons.ElementAtOrDefault(player.cursorIcon);
                var drawIcon = icon ?? CustomCursor.CursorTex;
                var iconRect = new Rect(pos, new Vector2(24f * drawIcon.width / drawIcon.height, 24f));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(pos, new Vector2(100, 30)).CenterOn(iconRect).Down(20f).Left(icon != null ? 0f : 5f), player.username);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                if (icon != null && Multiplayer.iconInfos[player.cursorIcon].hasStuff)
                    GUI.color = new Color(0.5f, 0.4f, 0.26f, 0.5f);

                GUI.DrawTexture(iconRect, drawIcon);

                GUI.color = Color.white;
            }
        }
    }

    [HarmonyPatch(typeof(NamePlayerFactionAndSettlementUtility), nameof(NamePlayerFactionAndSettlementUtility.CanNameAnythingNow))]
    static class NoNamingInMultiplayer
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [MpPatch(typeof(CameraJumper), nameof(CameraJumper.TrySelect))]
    [MpPatch(typeof(CameraJumper), nameof(CameraJumper.TryJumpAndSelect))]
    [MpPatch(typeof(CameraJumper), nameof(CameraJumper.TryJump), new[] { typeof(GlobalTargetInfo) })]
    static class NoCameraJumpingDuringSkipping
    {
        static bool Prefix() => TickPatch.skipTo < 0;
    }

    [HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
    static class WealthWatcherRecalc
    {
        static bool Prefix() => Multiplayer.Client == null || !Multiplayer.ShouldSync;
    }

    [HarmonyPatch(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.NextUnusedKeyFor))]
    static class ThinkTreeKeys
    {
        static MethodInfo RandInt = AccessTools.Method(typeof(Rand), "get_Int");

        // Replaces (^= Rand.Int) with (++)
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == RandInt)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    continue;
                }

                if (inst.opcode == OpCodes.Xor)
                    inst.opcode = OpCodes.Add;

                yield return inst;
            }
        }
    }

    static class CaptureThingSetMakers
    {
        public static List<ThingSetMaker> captured = new List<ThingSetMaker>();

        static void Prefix(ThingSetMaker __instance)
        {
            if (Current.ProgramState == ProgramState.Entry)
                captured.Add(__instance);
        }
    }

    [MpPatch(typeof(SubcameraDriver), nameof(SubcameraDriver.UpdatePositions))]
    [MpPatch(typeof(PortraitsCache), nameof(PortraitsCache.Get))]
    static class RenderTextureCreatePatch
    {
        static MethodInfo IsCreated = AccessTools.Method(typeof(RenderTexture), "IsCreated");
        static FieldInfo ArbiterField = AccessTools.Field(typeof(Multiplayer), nameof(Multiplayer.arbiterInstance));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == IsCreated)
                {
                    yield return new CodeInstruction(OpCodes.Ldsfld, ArbiterField);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }
    }

    [MpPatch(typeof(WaterInfo), nameof(WaterInfo.SetTextures))]
    [MpPatch(typeof(SubcameraDriver), nameof(SubcameraDriver.UpdatePositions))]
    [MpPatch(typeof(Prefs), nameof(Prefs.Save))]
    [MpPatch(typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode))]
    [MpPatch(typeof(Section), nameof(Section.RegenerateAllLayers))]
    [MpPatch(typeof(Section), nameof(Section.RegenerateLayers))]
    [MpPatch(typeof(SectionLayer), nameof(SectionLayer.DrawLayer))]
    [MpPatch(typeof(Map), nameof(Map.MapUpdate))]
    static class CancelForArbiter
    {
        static bool Prefix() => !Multiplayer.arbiterInstance;
    }

    [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
    static class CancelWorldRendererCtor
    {
        static bool Prefix() => !Multiplayer.arbiterInstance;

        static void Postfix(WorldRenderer __instance)
        {
            if (Multiplayer.arbiterInstance)
                __instance.layers = new List<WorldLayer>();
        }
    }

    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.VolumeGame))]
    [MpPatch(typeof(Prefs), nameof(Prefs.Save))]
    static class CancelDuringSkipping
    {
        static bool Prefix() => TickPatch.skipTo < 0;
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LetterStackUpdate))]
    static class CloseLetters
    {
        static void Postfix(LetterStack __instance)
        {
            if (Multiplayer.Client == null) return;
            if (TickPatch.skipTo < 0 && !Multiplayer.arbiterInstance) return;

            for (int i = __instance.letters.Count - 1; i >= 0; i--)
            {
                var letter = __instance.letters[i];
                if (letter is ChoiceLetter choice && choice.Choices.Any(c => c.action?.Method == choice.Option_Close.action.Method) && Time.time - letter.arrivalTime > 4)
                    __instance.RemoveLetter(letter);
            }
        }
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.MaxNumberOfPlayerSettlements), MethodType.Getter)]
    static class MaxColoniesPatch
    {
        static void Postfix(ref int __result)
        {
            if (Multiplayer.Client != null)
                __result = 5;
        }
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RunInBackground), MethodType.Getter)]
    static class RunInBackgroundPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = true;
        }
    }

    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnLoad))]
    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnError))]
    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnUrgentLetter))]
    static class PrefGettersInMultiplayer
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnLoad))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnError))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnUrgentLetter))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.MaxNumberOfPlayerSettlements))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.RunInBackground))]
    static class PrefSettersInMultiplayer
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(StorytellerUI), nameof(StorytellerUI.DrawStorytellerSelectionInterface))]
    static class DisableStorytellerSelection
    {
        static bool Prefix() => Multiplayer.Client == null || Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout;
    }

    [HarmonyPatch(typeof(Command_LoadToTransporter), nameof(Command_LoadToTransporter.ProcessInput))]
    static class DisableTransporterLoading
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            Messages.Message("Not available in multiplayer.", MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    static class ArbiterLongEventPatch
    {
        static void Postfix()
        {
            if (Multiplayer.arbiterInstance && LongEventHandler.currentEvent != null)
                LongEventHandler.currentEvent.alreadyDisplayed = true;
        }
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

    // todo does this cause issues?
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetHashCode))]
    static class TradeableHashCode
    {
        static bool Prefix() => false;

        static void Postfix(Tradeable __instance, ref int __result)
        {
            __result = RuntimeHelpers.GetHashCode(__instance);
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>) })]
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

        private static void Marker() { }

        public static bool IsTickMarked(Action action)
        {
            return (action as MulticastDelegate)?.GetInvocationList()?.Any(d => d.Method == MarkerMethod) ?? false;
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
                Multiplayer.Client.Send(Packets.Client_Pause, new object[] { true });
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteToExecuteWhenFinished))]
    static class LongEventEnd
    {
        static void Postfix()
        {
            if (Multiplayer.Client != null && NewLongEvent.currentEventWasMarked)
                Multiplayer.Client.Send(Packets.Client_Pause, new object[] { false });
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>) })]
    static class LongEventAlwaysSync
    {
        static void Prefix(ref bool doAsynchronously)
        {
            if (Multiplayer.ExecutingCmds)
                doAsynchronously = false;
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellForWorker))]
    static class FindBestStorageCellMarker
    {
        public static bool executing;

        static void Prefix() => executing = true;
        static void Postfix() => executing = false;
    }

    [HarmonyPatch(typeof(RandomNumberGenerator_BasicHash), nameof(RandomNumberGenerator_BasicHash.GetHash))]
    static class RandGetHashPatch
    {
        static void Postfix()
        {
            if (!MpVersion.IsDebug) return;

            if (Multiplayer.Client == null) return;
            if (RandPatches.Ignore) return;
            if (TickPatch.skipTo >= 0 || Multiplayer.IsReplay) return;

            if (!Multiplayer.Ticking && !Multiplayer.ExecutingCmds) return;

            if (!WildAnimalSpawnerTickMarker.ticking &&
                !WildPlantSpawnerTickMarker.ticking &&
                !SteadyEnvironmentEffectsTickMarker.ticking &&
                !FindBestStorageCellMarker.executing &&
                ThingContext.Current?.def != ThingDefOf.SteamGeyser)
                Multiplayer.game.sync.TryAddStackTrace();
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

    [HarmonyPatch(typeof(Archive), "<Add>m__2")]
    static class SortArchivablesById
    {
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

}