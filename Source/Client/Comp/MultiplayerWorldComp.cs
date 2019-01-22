using Harmony;
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

namespace Multiplayer.Client
{
    public class MultiplayerWorldComp : IExposable, ITickable
    {
        public static bool tickingWorld;
        public static bool executingCmdWorld;

        public float RealTimeToTickThrough { get; set; }

        public float TickRateMultiplier(TimeSpeed speed)
        {
            switch (speed)
            {
                case TimeSpeed.Paused:
                    return 0f;
                case TimeSpeed.Normal:
                    return 1f;
                case TimeSpeed.Fast:
                    return 3f;
                case TimeSpeed.Superfast:
                    return 6f;
                case TimeSpeed.Ultrafast:
                    return 15f;
                default:
                    return -1f;
            }
        }

        public TimeSpeed TimeSpeed
        {
            get => Find.TickManager.CurTimeSpeed;
            set => Find.TickManager.CurTimeSpeed = value;
        }

        public Queue<ScheduledCommand> Cmds { get => cmds; }

        public Dictionary<int, FactionWorldData> factionData = new Dictionary<int, FactionWorldData>();

        public World world;
        public IdBlock globalIdBlock;
        public ulong randState = 2;
        public bool asyncTime;
        public bool debugMode;
        public TileTemperaturesComp uiTemperatures;

        public List<MpTradeSession> trading = new List<MpTradeSession>();

        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();

        public MultiplayerWorldComp(World world)
        {
            this.world = world;
            this.uiTemperatures = new TileTemperaturesComp(world);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref TickPatch.Timer, "timer");
            Scribe_Values.Look(ref asyncTime, "asyncTime", true, true); // Enable async time on old saves
            Scribe_Values.Look(ref debugMode, "debugMode");
            ScribeUtil.LookULong(ref randState, "randState", 2);

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

            Multiplayer.ExposeIdBlock(ref globalIdBlock, "globalIdBlock");
        }

        private int currentFactionId;

        private void ExposeFactionData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int currentFactionId = Faction.OfPlayer.loadID;
                ScribeUtil.LookValue(currentFactionId, "currentFactionId");

                var factionData = new Dictionary<int, FactionWorldData>(this.factionData);
                factionData.Remove(currentFactionId);

                Scribe_Collections.Look(ref factionData, "factionData", LookMode.Value, LookMode.Deep);
            }
            else
            {
                // The faction whose data is currently set
                Scribe_Values.Look(ref currentFactionId, "currentFactionId");

                Scribe_Collections.Look(ref factionData, "factionData", LookMode.Value, LookMode.Deep);
                if (factionData == null)
                    factionData = new Dictionary<int, FactionWorldData>();
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars && Multiplayer.session != null && Multiplayer.game != null)
            {
                Multiplayer.game.myFactionLoading = Find.FactionManager.GetById(Multiplayer.session.myFactionId);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
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

                Multiplayer.game.sync.TryAddWorld(randState);
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
            UniqueIdsPatch.CurrentBlock = globalIdBlock;
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
            ByteReader data = new ByteReader(cmd.data);

            executingCmdWorld = true;
            TickPatch.currentExecutingCmdIssuedBySelf = cmd.issuedBySelf && !TickPatch.Skipping;

            PreContext();
            FactionContext.Push(cmd.GetFaction());

            bool devMode = Prefs.data.devMode;
            Prefs.data.devMode = Multiplayer.WorldComp.debugMode;

            try
            {
                if (cmdType == CommandType.Sync)
                {
                    Sync.HandleCmd(data);
                }

                if (cmdType == CommandType.DebugTools)
                {
                    MpDebugTools.HandleCmd(data);
                }

                if (cmdType == CommandType.WorldTimeSpeed)
                {
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();

                    Multiplayer.WorldComp.TimeSpeed = speed;

                    if (!asyncTime)
                    {
                        foreach (var map in Find.Maps)
                            map.AsyncTime().TimeSpeed = speed;

                        if (!cmd.issuedBySelf)
                            lastSpeedChange = Time.realtimeSinceStartup;
                    }

                    MpLog.Log("Set world speed " + speed + " " + TickPatch.Timer + " " + Find.TickManager.TicksGame);
                }

                if (cmdType == CommandType.SetupFaction)
                {
                    HandleSetupFaction(cmd, data);
                }

                if (cmdType == CommandType.FactionOffline)
                {
                    int factionId = data.ReadInt32();
                    Multiplayer.WorldComp.factionData[factionId].online = false;

                    if (Multiplayer.session.myFactionId == factionId)
                        Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;
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
                    LongEventHandler.QueueLongEvent(DoAutosave, "MpSaving", false, null);
                }
            }
            catch (Exception e)
            {
                Log.Error($"World cmd exception ({cmdType}): {e}");
            }
            finally
            {
                Prefs.data.devMode = devMode;

                FactionContext.Pop();
                PostContext();
                TickPatch.currentExecutingCmdIssuedBySelf = false;
                executingCmdWorld = false;

                Multiplayer.game.sync.TryAddCmd(randState);
            }
        }

        private static void DoAutosave()
        {
            var autosaveFile = AutosaveFile();
            var written = false;

            if (Multiplayer.LocalServer != null && !TickPatch.Skipping && !Multiplayer.IsReplay)
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

            XmlDocument doc = SaveLoad.SaveAndReload();

            if (!Multiplayer.session.resyncing)
            {
                SaveLoad.CacheGameData(doc);

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

            if (!TickPatch.Skipping && !Multiplayer.IsReplay && (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance))
                SaveLoad.SendCurrentGameData(true);
        }

        private static string AutosaveFile()
        {
            return Enumerable
                .Range(1, MultiplayerMod.settings.autosaveSlots)
                .Select(i => $"Autosave-{i}")
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
                    def = Multiplayer.FactionDef,
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

        public static FactionWorldData FromCurrent(int factionId = int.MinValue)
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
