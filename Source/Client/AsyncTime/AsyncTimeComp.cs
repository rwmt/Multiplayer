using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Saving;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
    public class AsyncTimeComp : IExposable, ITickable
    {
        public static Map tickingMap;
        public static Map executingCmdMap;

        public float TickRateMultiplier(TimeSpeed speed)
        {
            var comp = map.MpComp();

            var enforcePause = comp.sessionManager.IsAnySessionCurrentlyPausing(map) ||
                Multiplayer.WorldComp.sessionManager.IsAnySessionCurrentlyPausing(map);

            if (enforcePause)
                return 0f;

            if (mapTicks < slower.forceNormalSpeedUntil)
                return speed == TimeSpeed.Paused ? 0 : 1;

            switch (speed)
            {
                case TimeSpeed.Paused:
                    return 0f;
                case TimeSpeed.Normal:
                    return 1f;
                case TimeSpeed.Fast:
                    return 3f;
                case TimeSpeed.Superfast:
                    if (nothingHappeningCached)
                        return 12f;
                    return 6f;
                case TimeSpeed.Ultrafast:
                    return 15f;
                default:
                    return -1f;
            }
        }

        public TimeSpeed DesiredTimeSpeed
        {
            get => timeSpeedInt;
            set => timeSpeedInt = value;
        }

        public bool Paused => this.ActualRateMultiplier(DesiredTimeSpeed) == 0f;

        public float TimeToTickThrough { get; set; }

        public Queue<ScheduledCommand> Cmds => cmds;

        public int TickableId => map.uniqueID;

        public int GameStartAbsTick
        {
            get
            {
                if (gameStartAbsTickMap == 0)
                {
                    gameStartAbsTickMap = Find.TickManager?.gameStartAbsTick ?? 0;
                }

                return gameStartAbsTickMap;
            }
        }

        public Map map;
        public int mapTicks;
        private int gameStartAbsTickMap;
        private TimeSpeed timeSpeedInt;
        public bool forcedNormalSpeed;
        public int eventCount;

        public TimeSlower slower = new();

        public TickList tickListNormal = new(TickerType.Normal);
        public TickList tickListRare = new(TickerType.Rare);
        public TickList tickListLong = new(TickerType.Long);

        // Shared random state for ticking and commands
        public ulong randState;

        // Always empty: map commands are queued in AsyncWorldTimeComp.cmds to keep a single global order
        public Queue<ScheduledCommand> cmds = new();

        public int CurrentPlayerCount { get; private set; }
        public int VTR => CurrentPlayerCount > 0 ? VTRSync.MinimumVtr : VTRSync.MaximumVtr;

        public AsyncTimeComp(Map map, int gameStartAbsTick = 0)
        {
            this.map = map;
            this.gameStartAbsTickMap = gameStartAbsTick;

            // Use the world's constant rand seed and map tile ID as our initial randState.
            // Only fill the seed part, leave the iterations out.
            randState = (uint)Gen.HashCombineInt(map.uniqueID, Find.World.ConstantRandSeed);
        }

        public void Tick()
        {
            tickingMap = map;
            int randDepth = Rand.stateStack.Count;
            PreContext();

            //SimpleProfiler.Start();

            try
            {
                // Like vanilla's DoSingleTick, an exception in one subsystem shouldn't skip the rest of the tick
                // (in particular the mapTicks increment)
                try
                {
                    map.MapPreTick();
                }
                catch (Exception e)
                {
                    Log.Error($"Exception in MapPreTick of {map}: {e}");
                    SimulationFailures.Record("MapPreTick", e);
                }

                mapTicks++;
                Find.TickManager.ticksGameInt = mapTicks;

                //Either This line or PathFinderPatch can be commented out
                //ForeCompeleScheduledJobs here can prevent all Multithread Races,
                //but also force pathfinder to be done before tick runing
                //PathFinderPatch is the other way try to snapshot everything worker threads are using
                map.pathFinder.ForceCompleteScheduledJobs();

                tickListNormal.Tick();
                tickListRare.Tick();
                tickListLong.Tick();

                try
                {
                    TickMapSessions();
                }
                catch (Exception e)
                {
                    Log.Error($"Exception ticking sessions of {map}: {e}");
                    SimulationFailures.Record("map sessions", e);
                }

                try
                {
                    Find.Storyteller.StorytellerTick();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                    SimulationFailures.Record("storyteller", e);
                }

                try
                {
                    Find.StoryWatcher.StoryWatcherTick();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                    SimulationFailures.Record("story watcher", e);
                }

                try
                {
                    QuestManagerTickAsyncTime();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                    SimulationFailures.Record("async quests", e);
                }

                map.MapPostTick();
                Find.TickManager.ticksThisFrame = 1;
                map.postTickVisuals.ProcessPostTickVisuals();
                Find.TickManager.ticksThisFrame = 0;

                UpdateManagers();
                CacheNothingHappening();
            }
            finally
            {
                RestoreRandStack(randDepth + 1, this);
                PostContext();
                Multiplayer.game.sync.TryAddMapRandomState(map.uniqueID, randState);
                eventCount++;
                tickingMap = null;

                //SimpleProfiler.Pause();
            }
        }

        public void TickMapSessions()
        {
            map.MpComp().sessionManager.TickSessions();
        }

        // These are normally called in Map.MapUpdate() and react to changes in the game state even when the game is paused (not ticking)
        // Update() methods are not deterministic, but in multiplayer all game state changes (which don't happen during ticking) happen in commands
        // Thus these methods can be moved to Tick() and ExecuteCmd() by way of this method
        public void UpdateManagers()
        {
            map.regionGrid.UpdateClean();
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();

            map.powerNetManager.UpdatePowerNetsAndConnections_First();
            map.glowGrid.GlowGridUpdate_First();
        }

        private TimeSnapshot? prevTime;

        public void PreContext()
        {
            map.PushFaction(
                !Multiplayer.GameComp.multifaction || map.ParentFaction is { IsPlayer: true }
                    ? map.ParentFaction
                    : Multiplayer.WorldComp.spectatorFaction,
                force: true);

            prevTime = TimeSnapshot.GetAndSetFromMap(map);

            Rand.PushState();
            Rand.StateCompressed = randState;

            // Reset the effects of SkyManager.SkyManagerUpdate
            map.skyManager.curSkyGlowInt = map.skyManager.CurrentSkyTarget().glow;
        }

        public void PostContext()
        {
            prevTime?.Set();

            randState = Rand.StateCompressed;
            Rand.PopState();

            map.PopFaction();
        }

        // A Rand push left unpopped by an exception would otherwise be saved as the context's state, orphan the
        // context's own entry and keep the stack too deep (which disables desync traces) for the rest of the session
        public static void RestoreRandStack(int depth, object context)
        {
            int count = Rand.stateStack.Count;
            if (count == depth) return;

            // Too shallow (an extra pop) can't be repaired, only reported
            Log.ErrorOnce($"Multiplayer: unbalanced Rand push/pop in {context}: stack depth {count}, expected {depth}", 0x5A3D17 ^ depth);

            while (Rand.stateStack.Count > depth)
                Rand.PopState();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref mapTicks, "mapTicks");
            Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");

            Scribe_Values.Look(ref gameStartAbsTickMap, "gameStartAbsTickMap");

            Scribe_Custom.LookULong(ref randState, "randState", 1);

            // Affects the simulation through VTR. Saved so that it survives reloads, which can happen
            // on one client only (standalone join points).
            var playerCount = CurrentPlayerCount;
            Scribe_Values.Look(ref playerCount, "playerCount");
            CurrentPlayerCount = playerCount;
        }

        public int IncreasePlayerCount() => CurrentPlayerCount += 1;
        // This should never go below 0, this is just defensive programming. Hopefully not needed anymore, but
        // nevertheless still left.
        public int DecreasePlayerCount() => CurrentPlayerCount = Math.Max(0, CurrentPlayerCount - 1);

        public void FinalizeInit()
        {
            // Map commands live in the world's queue (see MultiplayerSession.ScheduleCommand) which is built only in
            // Loader.PostLoad. Rebuilding a queue here (possibly on the loading thread, or for a map generated during
            // play) used to drop or duplicate commands.
        }

        public static bool keepTheMap;
        public static List<object> prevSelected;

        public void ExecuteCmd(ScheduledCommand cmd)
        {
            CommandType cmdType = cmd.type;
            LoggingByteReader data = new LoggingByteReader(cmd.data);
            data.Log.Node($"{cmdType} Map {map.uniqueID}");

            MpContext context = data.MpContext();

            keepTheMap = false;
            var prevMap = Current.Game.CurrentMap;
            Current.Game.currentMapIndex = (sbyte)map.Index;

            executingCmdMap = map;
            TickPatch.currentExecutingCmdIssuedBySelf = cmd.IsIssuedBySelf() && !TickPatch.Simulating;
            TickPatch.currentExecutingCmdType = cmdType;

            int randDepth = Rand.stateStack.Count;
            PreContext();
            map.PushFaction(cmd.GetFaction(), force: true);

            context.map = map;

            prevSelected = Find.Selector.selected;
            Find.Selector.selected = new List<object>();

            SelectorDeselectPatch.deselected = new List<object>();

            bool prevDevMode = Prefs.data.devMode;
            bool prevGodMode = DebugSettings.godMode;
            Multiplayer.GameComp.playerData.GetValueOrDefault(cmd.playerId)?.SetContext();

            try
            {
                if (cmdType == CommandType.Sync)
                {
                    var handler = SyncUtil.HandleCmd(data);
                    data.Log.current.text = handler.ToString();
                }

                if (cmdType == CommandType.DebugTools)
                {
                    DebugSync.HandleCmd(data);
                }

                if (cmdType == CommandType.MapTimeSpeed && Multiplayer.GameComp.asyncTime)
                {
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();
                    DesiredTimeSpeed = speed;

                    MpLog.Debug("Set map time speed " + speed);
                }

                if (cmdType == CommandType.Designator)
                {
                    HandleDesignator(data);
                }

                UpdateManagers();
            }
            catch (Exception e)
            {
                MpLog.Error($"Map cmd exception ({cmdType}): {e}");
                SimulationFailures.Record($"map command {cmdType}", e);
            }
            finally
            {
                DebugSettings.godMode = prevGodMode;
                Prefs.data.devMode = prevDevMode;

                foreach (var deselected in SelectorDeselectPatch.deselected)
                    prevSelected.Remove(deselected);
                SelectorDeselectPatch.deselected = null;

                Find.Selector.selected = prevSelected;
                prevSelected = null;

                // Local UI, depends on the local selection
                try
                {
                    using (LocalOnlyScope.Enter())
                        Find.MainButtonsRoot.tabs.Notify_SelectedObjectDespawned();
                }
                catch (Exception e)
                {
                    Log.Error($"Exception updating main tabs after a command: {e}");
                }

                RestoreRandStack(randDepth + 1, this);
                map.PopFaction();
                PostContext();

                TickPatch.currentExecutingCmdIssuedBySelf = false;
                TickPatch.currentExecutingCmdType = null;
                executingCmdMap = null;

                if (!keepTheMap)
                    TrySetCurrentMap(prevMap);

                keepTheMap = false;

                Multiplayer.game.sync.TryAddCommandRandomState(randState);

                eventCount++;

                if (cmdType != CommandType.MapTimeSpeed)
                    Multiplayer.ReaderLog.AddCurrentNode(data);
            }
        }

        private static void TrySetCurrentMap(Map map)
        {
            if (!Find.Maps.Contains(map))
            {
                Current.Game.CurrentMap = Find.Maps.Any() ? Find.Maps[0] : null;
                Find.World.renderer.wantedMode = WorldRenderMode.Planet;
            }
            else
            {
                Current.Game.currentMapIndex = (sbyte)map.Index;
            }
        }

        private void HandleDesignator(ByteReader data)
        {
            Container<Area>? prevArea = null;

            bool SetState(Designator designator)
            {
                if (designator is Designator_AreaAllowed)
                {
                    Area area = SyncSerialization.ReadSync<Area>(data);
                    if (area == null) return false;

                    prevArea = Designator_AreaAllowed.selectedArea;
                    Designator_AreaAllowed.selectedArea = area;
                }

                if (designator is Designator_Install)
                {
                    Thing thing = SyncSerialization.ReadSync<Thing>(data);
                    if (thing == null) return false;

                    DesignatorInstall_SetThingToInstall.thingToInstall = thing;
                }

                if (designator is Designator_Zone)
                {
                    Zone zone = SyncSerialization.ReadSync<Zone>(data);
                    if (zone != null)
                        Find.Selector.selected.Add(zone);
                }

                if (designator is Designator_Plan_Add designatorPlanAdd)
                {
                    designatorPlanAdd.colorDef = SyncSerialization.ReadSync<ColorDef>(data);
                    designator = designatorPlanAdd;
                }

                if (designator is Designator_Plan_Copy designatorPlanCopy)
                {
                    designatorPlanCopy.cells.Clear();
                    designatorPlanCopy.cells.AddRange(SyncSerialization.ReadSync<List<IntVec3>>(data));
                    designator = designatorPlanCopy;
                }

                if (designator is Designator_Plan_CopySelectionPaste pasteDesignator)
                {
                    pasteDesignator.indices = SyncSerialization.ReadSync<CellIndices>(data);
                    pasteDesignator.rotation = SyncSerialization.ReadSync<Rot4>(data);

                    pasteDesignator.grid.Clear();
                    pasteDesignator.grid.AddRange(SyncSerialization.ReadSync<List<ColorDef>>(data));

                    pasteDesignator.colors.Clear();
                    pasteDesignator.colors.AddRange(SyncSerialization.ReadSync<HashSet<ColorDef>>(data));

                    designator = pasteDesignator;
                }

                return true;
            }

            void RestoreState()
            {
                if (prevArea.HasValue)
                    Designator_AreaAllowed.selectedArea = prevArea.Value.Inner;

                DesignatorInstall_SetThingToInstall.thingToInstall = null;
            }

            var mode = SyncSerialization.ReadSync<DesignatorMode>(data);
            var designator = SyncSerialization.ReadSync<Designator>(data);

            try
            {
                if (!SetState(designator)) return;

                if (mode == DesignatorMode.SingleCell)
                {
                    IntVec3 cell = SyncSerialization.ReadSync<IntVec3>(data);

                    designator.DesignateSingleCell(cell);
                    designator.Finalize(true);
                }
                else if (mode == DesignatorMode.MultiCell)
                {
                    IntVec3[] cells = SyncSerialization.ReadSync<IntVec3[]>(data);

                    designator.DesignateMultiCell(cells);
                }
                else if (mode == DesignatorMode.Thing)
                {
                    Thing thing = SyncSerialization.ReadSync<Thing>(data);
                    if (thing == null) return;

                    designator.DesignateThing(thing);
                    designator.Finalize(true);
                }

                SyncMethods.TryDirtyCurrentPawnTable(designator);
            }
            finally
            {
                RestoreState();
            }
        }

        private bool nothingHappeningCached;

        private void CacheNothingHappening()
        {
            nothingHappeningCached = true;
            var list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);

            foreach (var pawn in list)
            {
                if (pawn.HostFaction == null && pawn.RaceProps.Humanlike && pawn.Awake())
                    nothingHappeningCached = false;
            }

            if (nothingHappeningCached && map.IsPlayerHome && map.dangerWatcher.DangerRating >= StoryDanger.Low)
                nothingHappeningCached = false;
        }

        public override string ToString()
        {
            return $"{nameof(AsyncTimeComp)}_{map}";
        }

        public void QuestManagerTickAsyncTime()
        {
            if (!Multiplayer.GameComp.asyncTime || Paused) return;

            MultiplayerAsyncQuest.TickMapQuests(this);
        }
    }

    public enum DesignatorMode : byte
    {
        SingleCell,
        MultiCell,
        Thing
    }
}
