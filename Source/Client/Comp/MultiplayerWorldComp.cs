using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Comp;
using UnityEngine;
using Verse;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Saving;
using Multiplayer.Client.Util;

namespace Multiplayer.Client
{

    public class MultiplayerWorldComp : IExposable, ITickable
    {
        public static bool tickingWorld;
        public static bool executingCmdWorld;
        private TimeSpeed desiredTimeSpeed = TimeSpeed.Paused;

        public float RealTimeToTickThrough { get; set; }

        public float TickRateMultiplier(TimeSpeed speed)
        {
            if (Multiplayer.GameComp.asyncTime)
            {
                var enforcePause = Multiplayer.WorldComp.splitSession != null ||
                    AsyncTimeComp.pauseLocks.Any(x => x(null));

                if (enforcePause)
                    return 0f;
            }

            return speed switch
            {
                TimeSpeed.Paused => 0f,
                TimeSpeed.Normal => 1f,
                TimeSpeed.Fast => 3f,
                TimeSpeed.Superfast => 6f,
                TimeSpeed.Ultrafast => 15f,
                _ => -1f
            };
        }

        public TimeSpeed TimeSpeed
        {
            get => Find.TickManager.CurTimeSpeed;
            set {
                desiredTimeSpeed = value;
                UpdateTimeSpeed();
            }
        }

        /**
         * Clamps the World's TimeSpeed to be between (slowest map) and (fastest map)
         * Caution: doesn't work if called inside a MapAsyncTime.PreContext()
         */
        public void UpdateTimeSpeed()
        {
            if (!Multiplayer.GameComp.asyncTime) {
                Find.TickManager.CurTimeSpeed = desiredTimeSpeed;
                return;
            }

            var mapSpeeds = Find.Maps.Select(m => m.AsyncTime())
                .Where(a => a.ActualRateMultiplier(a.TimeSpeed) != 0f)
                .Select(a => a.TimeSpeed)
                .ToList();

            if (mapSpeeds.NullOrEmpty()) {
                // all maps are paused = pause the world
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            }
            else {
                Find.TickManager.CurTimeSpeed = (TimeSpeed)Math.Min(Math.Max((byte)desiredTimeSpeed, (byte)mapSpeeds.Min()), (byte)mapSpeeds.Max());
            }
        }

        public Queue<ScheduledCommand> Cmds => cmds;

        public int TickableId => -1;

        public Dictionary<int, FactionWorldData> factionData = new();

        public World world;
        public ulong randState = 2;
        public TileTemperaturesComp uiTemperatures;

        public List<MpTradeSession> trading = new();
        public CaravanSplittingSession splitSession;

        public Queue<ScheduledCommand> cmds = new();

        public MultiplayerWorldComp(World world)
        {
            this.world = world;
            this.uiTemperatures = new TileTemperaturesComp(world);
        }

        public void ExposeData()
        {
            var timer = TickPatch.Timer;
            Scribe_Values.Look(ref timer, "timer");
            TickPatch.SetTimer(timer);

            Scribe_Custom.LookULong(ref randState, "randState", 2);

            TimeSpeed timeSpeed = Find.TickManager.CurTimeSpeed;
            Scribe_Values.Look(ref timeSpeed, "timeSpeed");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Find.TickManager.CurTimeSpeed = timeSpeed;

            ExposeFactionData();

            Scribe_Collections.Look(ref trading, "tradingSessions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (trading.RemoveAll(t => t.trader == null || t.playerNegotiator == null) > 0)
                    Log.Message("Some trading sessions had null entries");
            }
        }

        private int currentFactionId;

        private void ExposeFactionData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int currentFactionId = Faction.OfPlayer.loadID;
                Scribe_Custom.LookValue(currentFactionId, "currentFactionId");

                var factionData = new Dictionary<int, FactionWorldData>(this.factionData);
                factionData.Remove(currentFactionId);

                Scribe_Collections.Look(ref factionData, "factionData", LookMode.Value, LookMode.Deep);
            }
            else
            {
                // The faction whose data is currently set
                Scribe_Values.Look(ref currentFactionId, "currentFactionId");

                Scribe_Collections.Look(ref factionData, "factionData", LookMode.Value, LookMode.Deep);
                factionData ??= new Dictionary<int, FactionWorldData>();
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars && Multiplayer.session != null && Multiplayer.game != null)
            {
                Multiplayer.game.myFactionLoading = Find.FactionManager.GetById(Multiplayer.session.myFactionId);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Game manager order?
                factionData[currentFactionId] = FactionWorldData.FromCurrent(currentFactionId);
            }
        }

        public void Tick()
        {
            tickingWorld = true;
            PreContext();

            try
            {
                Find.TickManager.DoSingleTick();
                TickWorldTrading();
            }
            finally
            {
                PostContext();
                tickingWorld = false;

                Multiplayer.game.sync.TryAddWorldRandomState(randState);
            }
        }

        public void TickWorldTrading()
        {
            for (int i = trading.Count - 1; i >= 0; i--)
            {
                var session = trading[i];
                if (session.playerNegotiator.Spawned) continue;

                if (session.ShouldCancel())
                    RemoveTradeSession(session);
            }
        }

        public void RemoveTradeSession(MpTradeSession session)
        {
            int index = trading.IndexOf(session);
            trading.Remove(session);
            Find.WindowStack?.WindowOfType<TradingWindow>()?.Notify_RemovedSession(index);
        }

        public void PreContext()
        {
            UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;
            Rand.PushState();
            Rand.StateCompressed = randState;
        }

        public void PostContext()
        {
            randState = Rand.StateCompressed;
            Rand.PopState();
            UniqueIdsPatch.CurrentBlock = null;
        }

        public void SetFaction(Faction faction)
        {
            if (!factionData.TryGetValue(faction.loadID, out FactionWorldData data))
                return;

            Game game = Current.Game;
            game.researchManager = data.researchManager;
            game.drugPolicyDatabase = data.drugPolicyDatabase;
            game.outfitDatabase = data.outfitDatabase;
            game.foodRestrictionDatabase = data.foodRestrictionDatabase;
            game.playSettings = data.playSettings;

            SyncResearch.researchSpeed = data.researchSpeed;
        }

        public static float lastSpeedChange;

        public void ExecuteCmd(ScheduledCommand cmd)
        {
            CommandType cmdType = cmd.type;
            LoggingByteReader data = new LoggingByteReader(cmd.data);
            data.Log.Node($"{cmdType} Global");

            executingCmdWorld = true;
            TickPatch.currentExecutingCmdIssuedBySelf = cmd.issuedBySelf && !TickPatch.Simulating;

            PreContext();
            Extensions.PushFaction(null, cmd.GetFaction());

            bool prevDevMode = Prefs.data.devMode;
            var prevGodMode = DebugSettings.godMode;
            Multiplayer.GameComp.playerData.GetValueOrDefault(cmd.playerId)?.SetContext();

            var randCalls1 = DeferredStackTracing.randCalls;

            try
            {
                if (cmdType == CommandType.Sync)
                {
                    var handler = SyncUtil.HandleCmd(data);
                    data.Log.current.text = handler.ToString();
                }

                if (cmdType == CommandType.DebugTools)
                {
                    MpDebugTools.HandleCmd(data);
                }

                if (cmdType == CommandType.GlobalTimeSpeed)
                {
                    HandleTimeSpeed(cmd, data);
                }

                if (cmdType == CommandType.TimeSpeedVote)
                {
                    HandleTimeVote(cmd, data);
                }

                if (cmdType == CommandType.PauseAll)
                {
                    SetTimeEverywhere(TimeSpeed.Paused);
                }

                if (cmdType == CommandType.SetupFaction)
                {
                    HandleSetupFaction(cmd, data);
                }

                if (cmdType == CommandType.CreateJoinPoint)
                {
                    LongEventHandler.QueueLongEvent(CreateJoinPoint, "MpCreatingJoinPoint", false, null);
                }

                if (cmdType == CommandType.InitPlayerData)
                {
                    var playerId = data.ReadInt32();
                    var canUseDevMode = data.ReadBool();
                    Multiplayer.GameComp.playerData[playerId] = new PlayerData { canUseDevMode = canUseDevMode };
                }
            }
            catch (Exception e)
            {
                Log.Error($"World cmd exception ({cmdType}): {e}");
            }
            finally
            {
                DebugSettings.godMode = prevGodMode;
                Prefs.data.devMode = prevDevMode;

                MpLog.Debug($"rand calls {DeferredStackTracing.randCalls - randCalls1}");
                MpLog.Debug("rand state " + Rand.StateCompressed);

                Extensions.PopFaction();
                PostContext();
                TickPatch.currentExecutingCmdIssuedBySelf = false;
                executingCmdWorld = false;

                Multiplayer.game.sync.TryAddCommandRandomState(randState);

                if (cmdType != CommandType.GlobalTimeSpeed)
                    Multiplayer.ReaderLog.AddCurrentNode(data);
            }
        }

        private static void CreateJoinPoint()
        {
            Multiplayer.session.dataSnapshot = SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveAndReload());

            if (!TickPatch.Simulating && !Multiplayer.IsReplay && (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance))
                SaveLoad.SendGameData(Multiplayer.session.dataSnapshot, true);
        }

        public void SetTimeEverywhere(TimeSpeed speed)
        {
            foreach (var map in Find.Maps)
                map.AsyncTime().TimeSpeed = speed;
            TimeSpeed = speed;
        }

        private void HandleTimeSpeed(ScheduledCommand cmd, ByteReader data)
        {
            TimeSpeed speed = (TimeSpeed)data.ReadByte();

            Multiplayer.WorldComp.TimeSpeed = speed;

            if (!Multiplayer.GameComp.asyncTime)
            {
                SetTimeEverywhere(speed);

                if (!cmd.issuedBySelf)
                    lastSpeedChange = Time.realtimeSinceStartup;
            }

            MpLog.Debug($"Set world speed {speed} {TickPatch.Timer} {Find.TickManager.TicksGame}");
        }

        private void HandleTimeVote(ScheduledCommand cmd, ByteReader data)
        {
            TimeVote vote = (TimeVote)data.ReadByte();
            int tickableId = data.ReadInt32();

            if (vote >= TimeVote.Reset)
                Multiplayer.GameComp.playerData.Do(p => p.Value.SetTimeVote(tickableId, vote));
            else if (Multiplayer.GameComp.playerData.GetValueOrDefault(cmd.playerId) is { } playerData)
                playerData.SetTimeVote(tickableId, vote);

            if (!Multiplayer.GameComp.asyncTime || vote == TimeVote.ResetAll)
                SetTimeEverywhere(Multiplayer.GameComp.GetLowestTimeVote(Multiplayer.WorldComp.TickableId));
            else if (TickPatch.TickableById(tickableId) is { } tickable)
                tickable.TimeSpeed = Multiplayer.GameComp.GetLowestTimeVote(tickableId);

            UpdateTimeSpeed();
        }

        private void HandleSetupFaction(ScheduledCommand command, ByteReader data)
        {
            int factionId = data.ReadInt32();
            Faction faction = Find.FactionManager.GetById(factionId);

            if (faction == null)
            {
                faction = new Faction
                {
                    loadID = factionId,
                    def = FactionDefOf.PlayerColony,
                    Name = "Multiplayer faction",
                };

                Find.FactionManager.Add(faction);

                foreach (Faction current in Find.FactionManager.AllFactionsListForReading)
                {
                    if (current == faction) continue;
                    current.TryMakeInitialRelationsWith(faction);
                }

                Multiplayer.WorldComp.factionData[factionId] = FactionWorldData.New(factionId);

                MpLog.Log($"New faction {faction.GetUniqueLoadID()}");
            }
        }

        public void DirtyColonyTradeForMap(Map map)
        {
            if (map == null) return;
            foreach (MpTradeSession session in trading)
                if (session.playerNegotiator.Map == map)
                    session.deal.recacheColony = true;
        }

        public void DirtyTraderTradeForTrader(ITrader trader)
        {
            if (trader == null) return;
            foreach (MpTradeSession session in trading)
                if (session.trader == trader)
                    session.deal.recacheTrader = true;
        }

        public void DirtyTradeForSpawnedThing(Thing t)
        {
            if (t is not { Spawned: true }) return;
            foreach (MpTradeSession session in trading)
                if (session.playerNegotiator.Map == t.Map)
                    session.deal.recacheThings.Add(t);
        }

        public bool AnyTradeSessionsOnMap(Map map)
        {
            foreach (MpTradeSession session in trading)
                if (session.playerNegotiator.Map == map)
                    return true;
            return false;
        }

        public void FinalizeInit()
        {
            Multiplayer.game.SetThingMakerSeed((int)(randState >> 32));
        }

        public override string ToString()
        {
            return $"{nameof(MultiplayerWorldComp)}_{world}";
        }
    }

    public class FactionWorldData : IExposable
    {
        public int factionId;
        public bool online;

        public ResearchManager researchManager;
        public OutfitDatabase outfitDatabase;
        public DrugPolicyDatabase drugPolicyDatabase;
        public FoodRestrictionDatabase foodRestrictionDatabase;
        public PlaySettings playSettings;

        public ResearchSpeed researchSpeed;

        public FactionWorldData() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Values.Look(ref online, "online");

            Scribe_Deep.Look(ref researchManager, "researchManager");
            Scribe_Deep.Look(ref drugPolicyDatabase, "drugPolicyDatabase");
            Scribe_Deep.Look(ref outfitDatabase, "outfitDatabase");
            Scribe_Deep.Look(ref foodRestrictionDatabase, "foodRestrictionDatabase");
            Scribe_Deep.Look(ref playSettings, "playSettings");

            Scribe_Deep.Look(ref researchSpeed, "researchSpeed");
        }

        public void Tick()
        {
        }

        public static FactionWorldData New(int factionId)
        {
            return new FactionWorldData()
            {
                factionId = factionId,

                researchManager = new ResearchManager(),
                drugPolicyDatabase = new DrugPolicyDatabase(),
                outfitDatabase = new OutfitDatabase(),
                foodRestrictionDatabase = new FoodRestrictionDatabase(),
                playSettings = new PlaySettings(),
                researchSpeed = new ResearchSpeed(),
            };
        }

        public static FactionWorldData FromCurrent(int factionId)
        {
            return new FactionWorldData()
            {
                factionId = factionId == int.MinValue ? Faction.OfPlayer.loadID : factionId,
                online = true,

                researchManager = Find.ResearchManager,
                drugPolicyDatabase = Current.Game.drugPolicyDatabase,
                outfitDatabase = Current.Game.outfitDatabase,
                foodRestrictionDatabase = Current.Game.foodRestrictionDatabase,
                playSettings = Current.Game.playSettings,

                researchSpeed = new ResearchSpeed(),
            };
        }
    }
}
