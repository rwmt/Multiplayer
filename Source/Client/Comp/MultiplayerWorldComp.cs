using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
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
                    Multiplayer.WorldComp.globalDialogs.Any(d => d.attachedLetter == null || (d.attachedLetter.TimeoutActive && d.attachedLetter.disappearAtTick >= Find.TickManager.TicksGame));

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

        public Dictionary<int, FactionWorldData> factionData = new Dictionary<int, FactionWorldData>();

        public World world;
        public ulong randState = 2;
        public TileTemperaturesComp uiTemperatures;

        public List<MpTradeSession> trading = new List<MpTradeSession>();
        public CaravanSplittingSession splitSession;
        public List<PersistentDialog> globalDialogs = new();

        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();

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
            
            Scribe_Collections.Look(ref globalDialogs, nameof(globalDialogs), LookMode.Deep, null);
            if (Scribe.mode == LoadSaveMode.LoadingVars && globalDialogs == null)
                globalDialogs = new();
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

            bool devMode = Prefs.data.devMode;
            Prefs.data.devMode = Multiplayer.GameComp.debugMode;

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

                if (cmdType == CommandType.WorldTimeSpeed)
                {
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();

                    Multiplayer.WorldComp.TimeSpeed = speed;

                    if (!Multiplayer.GameComp.asyncTime)
                    {
                        foreach (var map in Find.Maps)
                            map.AsyncTime().TimeSpeed = speed;

                        if (!cmd.issuedBySelf)
                            lastSpeedChange = Time.realtimeSinceStartup;
                    }

                    MpLog.Log($"Set world speed {speed} {TickPatch.Timer} {Find.TickManager.TicksGame}");
                }

                if (cmdType == CommandType.SetupFaction)
                {
                    HandleSetupFaction(cmd, data);
                }

                if (cmdType == CommandType.FactionOffline)
                {
                    int factionId = data.ReadInt32();
                    //Multiplayer.WorldComp.factionData[factionId].online = false;

                    //if (Multiplayer.session.myFactionId == factionId)
                    //    Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;
                }

                if (cmdType == CommandType.FactionOnline)
                {
                    int factionId = data.ReadInt32();
                    Multiplayer.WorldComp.factionData[factionId].online = true;

                    if (Multiplayer.session.myFactionId == factionId)
                        Multiplayer.RealPlayerFaction = Find.FactionManager.AllFactionsListForReading.Find(f => f.loadID == factionId);
                }

                if (cmdType == CommandType.Autosave)
                {
                    LongEventHandler.QueueLongEvent(() => DoAutosave(data.ReadString()), "MpSaving", false, null);
                }
            }
            catch (Exception e)
            {
                Log.Error($"World cmd exception ({cmdType}): {e}");
            }
            finally
            {
                Prefs.data.devMode = devMode;

                Log.Message($"rand calls {DeferredStackTracing.randCalls - randCalls1}");
                Log.Message("rand state " + Rand.StateCompressed);

                Extensions.PopFaction();
                PostContext();
                TickPatch.currentExecutingCmdIssuedBySelf = false;
                executingCmdWorld = false;

                Multiplayer.game.sync.TryAddCommandRandomState(randState);

                if (cmdType != CommandType.WorldTimeSpeed)
                    Multiplayer.ReaderLog.AddCurrentNode(data);
            }
        }

        private static void DoAutosave(string saveName="")
        {
            var autosaveFile = saveName != "" ? saveName : AutosaveFile();
            var written = false;

            if (Multiplayer.LocalServer != null && !TickPatch.Simulating && !Multiplayer.IsReplay)
            {
                try
                {
                    var replay = Replay.ForSaving(autosaveFile);
                    replay.File.Delete();
                    replay.WriteCurrentData();
                    written = true;
                }
                catch (Exception e)
                {
                    Log.Error($"Writing first section of the autosave failed: {e}");
                }
            }

            var data = SaveLoad.SaveAndReload();

            if (!Multiplayer.session.resyncing)
            {
                SaveLoad.CacheGameData(data);

                if (written)
                {
                    try
                    {
                        var replay = Replay.ForSaving(autosaveFile);
                        replay.LoadInfo();
                        replay.WriteCurrentData();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Writing second section of the autosave failed: {e}");
                    }
                }
            }

            if (!TickPatch.Simulating && !Multiplayer.IsReplay && (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance))
                SaveLoad.SendCurrentGameData(true);
        }

        private static string AutosaveFile()
        {
            var autosavePrefix = "Autosave-";

            if (Multiplayer.settings.appendNameToAutosave)
            {
                autosavePrefix += $"{Multiplayer.session.gameName}-";
            }

            return Enumerable
                .Range(1, Multiplayer.settings.autosaveSlots)
                .Select(i => $"{autosavePrefix}{i}")
                .OrderBy(s => new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{s}.zip")).LastWriteTime)
                .First();
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
                    centralMelanin = Rand.Value
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
            foreach (MpTradeSession session in trading.Where(s => s.playerNegotiator.Map == map))
                session.deal.recacheColony = true;
        }

        public void DirtyTraderTradeForTrader(ITrader trader)
        {
            if (trader == null) return;
            foreach (MpTradeSession session in trading.Where(s => s.trader == trader))
                session.deal.recacheTrader = true;
        }

        public void DirtyTradeForSpawnedThing(Thing t)
        {
            if (t == null || !t.Spawned) return;

            foreach (MpTradeSession session in trading.Where(s => s.playerNegotiator.Map == t.Map))
                session.deal.recacheThings.Add(t);
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
