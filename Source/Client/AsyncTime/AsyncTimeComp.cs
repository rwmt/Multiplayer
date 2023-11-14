using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Multiplayer.API;
using Verse;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Saving;
using Multiplayer.Client.Util;

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

        public TimeSpeed DesiredTimeSpeed => timeSpeedInt;

        public void SetDesiredTimeSpeed(TimeSpeed speed)
        {
            timeSpeedInt = speed;
        }

        public bool Paused => this.ActualRateMultiplier(DesiredTimeSpeed) == 0f;

        public float TimeToTickThrough { get; set; }

        public Queue<ScheduledCommand> Cmds => cmds;

        public int TickableId => map.uniqueID;

        public Map map;
        public int mapTicks;
        private TimeSpeed timeSpeedInt;
        public bool forcedNormalSpeed;
        public int eventCount;

        public Storyteller storyteller;
        public StoryWatcher storyWatcher;
        public TimeSlower slower = new();

        public TickList tickListNormal = new(TickerType.Normal);
        public TickList tickListRare = new(TickerType.Rare);
        public TickList tickListLong = new(TickerType.Long);

        // Shared random state for ticking and commands
        public ulong randState = 1;

        public Queue<ScheduledCommand> cmds = new();

        public AsyncTimeComp(Map map)
        {
            this.map = map;
        }

        public void Tick()
        {
            tickingMap = map;
            PreContext();

            //SimpleProfiler.Start();

            try
            {
                map.MapPreTick();
                mapTicks++;
                Find.TickManager.ticksGameInt = mapTicks;

                tickListNormal.Tick();
                tickListRare.Tick();
                tickListLong.Tick();

                TickMapSessions();

                storyteller.StorytellerTick();
                storyWatcher.StoryWatcherTick();

                QuestManagerTickAsyncTime();

                map.MapPostTick();
                Find.TickManager.ticksThisFrame = 1;
                map.postTickVisuals.ProcessPostTickVisuals();
                Find.TickManager.ticksThisFrame = 0;

                UpdateManagers();
                CacheNothingHappening();
            }
            finally
            {
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
        private Storyteller prevStoryteller;
        private StoryWatcher prevStoryWatcher;

        public void PreContext()
        {
            if (Multiplayer.GameComp.multifaction)
            {
                map.PushFaction(map.ParentFaction is { IsPlayer: true }
                    ? map.ParentFaction
                    : Multiplayer.WorldComp.spectatorFaction);
            }

            prevTime = TimeSnapshot.GetAndSetFromMap(map);

            prevStoryteller = Current.Game.storyteller;
            prevStoryWatcher = Current.Game.storyWatcher;

            Current.Game.storyteller = storyteller;
            Current.Game.storyWatcher = storyWatcher;

            Rand.PushState();
            Rand.StateCompressed = randState;

            // Reset the effects of SkyManager.SkyManagerUpdate
            map.skyManager.curSkyGlowInt = map.skyManager.CurrentSkyTarget().glow;
        }

        public void PostContext()
        {
            Current.Game.storyteller = prevStoryteller;
            Current.Game.storyWatcher = prevStoryWatcher;

            prevTime?.Set();

            randState = Rand.StateCompressed;
            Rand.PopState();

            if (Multiplayer.GameComp.multifaction)
                map.PopFaction();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref mapTicks, "mapTicks");
            Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");

            Scribe_Deep.Look(ref storyteller, "storyteller");

            Scribe_Deep.Look(ref storyWatcher, "storyWatcher");
            if (Scribe.mode == LoadSaveMode.LoadingVars && storyWatcher == null)
                storyWatcher = new StoryWatcher();

            Scribe_Custom.LookULong(ref randState, "randState", 1);
        }

        public void FinalizeInit()
        {
            cmds = new Queue<ScheduledCommand>(
                Multiplayer.session.dataSnapshot?.MapCmds.GetValueSafe(map.uniqueID) ?? new List<ScheduledCommand>()
            );

            Log.Message($"Init map with cmds {cmds.Count}");
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
            TickPatch.currentExecutingCmdIssuedBySelf = cmd.issuedBySelf && !TickPatch.Simulating;

            PreContext();
            map.PushFaction(cmd.GetFaction());

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
                    SetDesiredTimeSpeed(speed);

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

                Find.MainButtonsRoot.tabs.Notify_SelectedObjectDespawned();

                map.PopFaction();
                PostContext();

                TickPatch.currentExecutingCmdIssuedBySelf = false;
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
