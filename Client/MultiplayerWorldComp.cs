using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerWorldComp : WorldComponent, ITickable
    {
        public static bool tickingWorld;
        public static bool executingCmdWorld;

        public float RealTimeToTickThrough { get; set; }

        public float CurTimePerTick
        {
            get
            {
                if (TickRateMultiplier == 0f)
                    return 0f;
                return 1f / TickRateMultiplier;
            }
        }

        public float TickRateMultiplier
        {
            get
            {
                if (!TickPatch.asyncTime) return Find.TickManager.TickRateMultiplier;

                switch (timeSpeedInt)
                {
                    case TimeSpeed.Normal:
                        return 1f;
                    case TimeSpeed.Fast:
                        return 3f;
                    case TimeSpeed.Superfast:
                        return 6f;
                    // todo speed up when nothing is happening
                    case TimeSpeed.Ultrafast:
                        return 15f;
                    default:
                        return 0f;
                }
            }
        }

        public TimeSpeed TimeSpeed
        {
            get => TickPatch.asyncTime ? timeSpeedInt : Find.TickManager.CurTimeSpeed;
            set
            {
                if (TickPatch.asyncTime)
                    timeSpeedInt = value;
                else
                    Find.TickManager.CurTimeSpeed = value;
            }
        }

        public Queue<ScheduledCommand> Cmds { get => cmds; }

        public Dictionary<int, FactionWorldData> factionData = new Dictionary<int, FactionWorldData>();

        public ConstantTicker ticker = new ConstantTicker();
        public IdBlock globalIdBlock;
        public string worldId = Guid.NewGuid().ToString();
        public int sessionId = new Random().Next();
        private TimeSpeed timeSpeedInt;

        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();

        public MultiplayerWorldComp(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref worldId, "worldId");
            Scribe_Values.Look(ref TickPatch.timerInt, "timer");
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");

            ExposeFactionData();

            Multiplayer.ExposeIdBlock(ref globalIdBlock, "globalIdBlock");
        }

        private void ExposeFactionData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var factionData = new Dictionary<int, FactionWorldData>(this.factionData);
                factionData.Remove(Multiplayer.DummyFaction.loadID);

                ScribeUtil.Look(ref factionData, "factionData", LookMode.Deep);
            }
            else
            {
                ScribeUtil.Look(ref factionData, "factionData", LookMode.Deep);
                if (factionData == null)
                    factionData = new Dictionary<int, FactionWorldData>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factionData[Multiplayer.DummyFaction.loadID] = FactionWorldData.FromCurrent();
            }
        }

        public void Tick()
        {
            tickingWorld = true;
            UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;
            Find.TickManager.CurTimeSpeed = TimeSpeed;

            try
            {
                Find.TickManager.DoSingleTick();
            }
            finally
            {
                UniqueIdsPatch.CurrentBlock = null;
                tickingWorld = false;
            }
        }

        public void SetFaction(Faction faction)
        {
            if (!factionData.TryGetValue(faction.loadID, out FactionWorldData data))
            {
                if (!Multiplayer.simulating)
                    MpLog.Log("No world faction data for faction {0} {1}", faction.loadID, faction);
                return;
            }

            Game game = Current.Game;
            game.researchManager = data.researchManager;
            game.drugPolicyDatabase = data.drugPolicyDatabase;
            game.outfitDatabase = data.outfitDatabase;
            game.playSettings = data.playSettings;

            SyncResearch.researchSpeed = data.researchSpeed;
        }

        public void ExecuteCmd(ScheduledCommand cmd)
        {
            ByteReader data = new ByteReader(cmd.data);

            executingCmdWorld = true;
            TickPatch.currentExecutingCmdIssuedBySelf = cmd.issuedBySelf;

            Rand.PushState();
            Multiplayer.Seed = Find.TickManager.TicksGame;
            CommandType cmdType = cmd.type;

            UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;
            FactionContext.Push(cmd.GetFaction());

            try
            {
                if (cmdType == CommandType.SYNC)
                {
                    Sync.HandleCmd(data);
                }

                if (cmdType == CommandType.WORLD_TIME_SPEED)
                {
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();
                    Multiplayer.WorldComp.TimeSpeed = speed;

                    MpLog.Log("Set world speed " + speed + " " + TickPatch.Timer + " " + Find.TickManager.TicksGame);
                }

                if (cmdType == CommandType.SETUP_FACTION)
                {
                    HandleSetupFaction(cmd, data);
                }

                if (cmdType == CommandType.FACTION_OFFLINE)
                {
                    int factionId = data.ReadInt32();
                    Multiplayer.WorldComp.factionData[factionId].online = false;

                    if (Multiplayer.session.myFactionId == factionId)
                        Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;
                }

                if (cmdType == CommandType.FACTION_ONLINE)
                {
                    int factionId = data.ReadInt32();
                    Multiplayer.WorldComp.factionData[factionId].online = true;

                    if (Multiplayer.session.myFactionId == factionId)
                        Multiplayer.RealPlayerFaction = Find.FactionManager.AllFactionsListForReading.Find(f => f.loadID == factionId);
                }

                if (cmdType == CommandType.AUTOSAVE)
                {
                    Multiplayer.WorldComp.TimeSpeed = TimeSpeed.Paused;

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        OnMainThread.ClearCaches();

                        XmlDocument doc = Multiplayer.SaveAndReload();
                        //Multiplayer.CacheAndSendGameData(doc);
                    }, "Autosaving", false, null);
                }
            }
            catch (Exception e)
            {
                Log.Error($"World cmd exception ({cmdType}): {e}");
            }
            finally
            {
                FactionContext.Pop();
                UniqueIdsPatch.CurrentBlock = null;
                Rand.PopState();
                TickPatch.currentExecutingCmdIssuedBySelf = false;
                executingCmdWorld = false;
            }
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
                    def = Multiplayer.factionDef,
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

                MpLog.Log("New faction {0}", faction.GetUniqueLoadID());
            }
        }
    }

    public class FactionWorldData : IExposable
    {
        public int factionId;
        public bool online;

        public ResearchManager researchManager;
        public DrugPolicyDatabase drugPolicyDatabase;
        public OutfitDatabase outfitDatabase;
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
                playSettings = new PlaySettings(),
                researchSpeed = new ResearchSpeed(),
            };
        }

        public static FactionWorldData FromCurrent()
        {
            return new FactionWorldData()
            {
                factionId = Faction.OfPlayer.loadID,
                online = true,

                researchManager = Find.ResearchManager,
                drugPolicyDatabase = Current.Game.drugPolicyDatabase,
                outfitDatabase = Current.Game.outfitDatabase,
                playSettings = Current.Game.playSettings,

                researchSpeed = new ResearchSpeed(),
            };
        }

        public static XmlDocument ExtractFromGameDoc(XmlDocument gameDoc)
        {
            XmlDocument doc = new XmlDocument();
            doc.AppendChild(doc.CreateElement("factionWorldData"));
            XmlNode root = doc.DocumentElement;

            string[] fromGame = new[] {
                "researchManager",
                "drugPolicyDatabase",
                "outfitDatabase",
                "playSettings",
                "history"
            };

            string[] fromWorld = new[] {
                "settings"
            };

            foreach (string s in fromGame)
                root.AppendChild(doc.ImportNode(gameDoc.DocumentElement["game"][s], true));

            foreach (string s in fromWorld)
                root.AppendChild(doc.ImportNode(gameDoc.DocumentElement["game"]["world"][s], true));

            return doc;
        }
    }
}
