using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.BaseGen;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class MultiplayerSession : IConnectionStatusListener
    {
        public string gameName;
        public int playerId;

        public IConnection client;
        public NetManager netClient;
        public PacketLogWindow packetLog = new PacketLogWindow();
        public int myFactionId;
        public List<PlayerInfo> players = new List<PlayerInfo>();

        public bool replay;
        public int replayTimerStart = -1;
        public int replayTimerEnd = -1;
        public List<ReplayEvent> events = new List<ReplayEvent>();

        public bool desynced;
        public bool resyncing;

        public MpDisconnectReason disconnectReason;
        public string disconnectReasonKey;
        public string disconnectInfo;

        public SessionModInfo mods = new SessionModInfo();

        public bool allowSteam;
        public List<CSteamID> pendingSteam = new List<CSteamID>();
        public List<CSteamID> knownUsers = new List<CSteamID>();
        public Thread steamNet;

        public const int MaxMessages = 200;
        public List<ChatMsg> messages = new List<ChatMsg>();
        public Rect chatPos;
        public bool hasUnread;

        public MultiplayerServer localServer;
        public Thread serverThread;
        public ServerSettings localSettings;

        public Process arbiter;
        public bool ArbiterPlaying => players.Any(p => p.type == PlayerType.Arbiter && p.status == PlayerStatus.Playing);

        public void Stop()
        {
            if (client != null)
            {
                client.Close();
                client.State = ConnectionStateEnum.Disconnected;
            }

            if (localServer != null)
            {
                localServer.running = false;
                serverThread.Join();
            }

            if (netClient != null)
                netClient.Stop();

            if (arbiter != null)
            {
                arbiter.TryKill();
                arbiter = null;
            }

            Log.Message("Multiplayer session stopped.");
        }

        public PlayerInfo GetPlayerInfo(int id)
        {
            return players.Find(p => p.id == id);
        }

        public void AddMsg(string msg, bool notify = true)
        {
            AddMsg(new ChatMsg_Text(msg), notify);
        }

        public void AddMsg(ChatMsg msg, bool notify = true)
        {
            var window = ChatWindow.Opened;

            if (window != null)
                window.OnChatReceived();
            else if (notify)
                NotifyChat();

            messages.Add(msg);

            if (messages.Count > MaxMessages)
                messages.RemoveAt(0);
        }

        public void NotifyChat()
        {
            hasUnread = true;
            SoundDefOf.PageChange.PlayOneShotOnCamera(null);
        }

        public void HandleDisconnectReason(MpDisconnectReason reason, byte[] data)
        {
            var reader = new ByteReader(data);
            string reasonKey = null;
            string descKey = null;

            if (reason == MpDisconnectReason.Generic) reasonKey = reader.ReadString();

            if (reason == MpDisconnectReason.Protocol)
            {
                reasonKey = "MpWrongProtocol";

                string strVersion = data.Length != 0 ? reader.ReadString() : "0.4.2";
                int proto = data.Length != 0 ? reader.ReadInt32() : 11;

                disconnectInfo = "MpWrongMultiplayerVersionInfo".Translate(strVersion, proto);
            }

            if (reason == MpDisconnectReason.UsernameLength) { reasonKey = "MpInvalidUsernameLength"; descKey = "MpChangeUsernameInfo"; }
            if (reason == MpDisconnectReason.UsernameChars) { reasonKey = "MpInvalidUsernameChars"; descKey = "MpChangeUsernameInfo"; }
            if (reason == MpDisconnectReason.UsernameAlreadyOnline) { reasonKey = "MpInvalidUsernameAlreadyPlaying"; descKey = "MpChangeUsernameInfo"; }
            if (reason == MpDisconnectReason.ServerClosed) reasonKey = "MpServerClosed";
            if (reason == MpDisconnectReason.ServerFull) reasonKey = "MpServerFull";
            if (reason == MpDisconnectReason.Kick) reasonKey = "MpKicked";

            if (reason == MpDisconnectReason.Defs)
            {
                foreach (var local in mods.defInfo)
                    local.Value.status = (DefCheckStatus)reader.ReadByte();
            }

            disconnectReason = reason;
            disconnectReasonKey = reasonKey?.Translate();
            disconnectInfo = disconnectInfo ?? descKey?.Translate();
        }

        public void Connected()
        {
        }

        public void Disconnected()
        {
            Find.WindowStack.windows.Clear();

            if (disconnectReason != MpDisconnectReason.Defs)
                Find.WindowStack.Add(new DisconnectedWindow(disconnectReasonKey, disconnectInfo) { returnToServerBrowser = Multiplayer.Client.State != ConnectionStateEnum.ClientPlaying });
            else
                Find.WindowStack.Add(new DefMismatchWindow(mods));
        }

        public void ReapplyPrefs()
        {
            Application.runInBackground = true;
        }
    }

    public class SessionModInfo
    {
        public string remoteRwVersion;
        public string[] remoteModNames;
        public Dictionary<string, DefInfo> defInfo;
    }

    public class PlayerInfo
    {
        public static readonly Vector3 Invalid = new Vector3(-1, 0, -1);

        public int id;
        public string username;
        public int latency;
        public int ticksBehind;
        public PlayerType type;
        public PlayerStatus status;

        public ulong steamId;
        public string steamPersonaName;

        public byte cursorSeq;
        public byte map = byte.MaxValue;
        public Vector3 cursor;
        public Vector3 lastCursor;
        public double updatedAt;
        public double lastDelta;
        public byte cursorIcon;
        public Vector3 dragStart = Invalid;

        public Dictionary<int, float> selectedThings = new Dictionary<int, float>();

        private PlayerInfo(int id, string username, int latency, PlayerType type)
        {
            this.id = id;
            this.username = username;
            this.latency = latency;
            this.type = type;
        }

        public static PlayerInfo Read(ByteReader data)
        {
            int id = data.ReadInt32();
            string username = data.ReadString();
            int latency = data.ReadInt32();
            var type = (PlayerType)data.ReadByte();
            var status = (PlayerStatus)data.ReadByte();

            var steamId = data.ReadULong();
            var steamName = data.ReadString();

            var ticksBehind = data.ReadInt32();

            return new PlayerInfo(id, username, latency, type)
            {
                status = status,
                steamId = steamId,
                steamPersonaName = steamName,
                ticksBehind = ticksBehind
            };
        }
    }

    public class MultiplayerGame
    {
        public SyncInfoBuffer sync = new SyncInfoBuffer();

        public MultiplayerWorldComp worldComp;
        public List<MultiplayerMapComp> mapComps = new List<MultiplayerMapComp>();
        public List<MapAsyncTimeComp> asyncTimeComps = new List<MapAsyncTimeComp>();
        public SharedCrossRefs sharedCrossRefs = new SharedCrossRefs();

        public Faction dummyFaction;
        private Faction myFaction;
        public Faction myFactionLoading;

        public Dictionary<int, PlayerDebugState> playerDebugState = new Dictionary<int, PlayerDebugState>();

        public Faction RealPlayerFaction
        {
            get => myFaction ?? myFactionLoading;

            set
            {
                myFaction = value;
                Faction.OfPlayer.def = Multiplayer.FactionDef;
                value.def = FactionDefOf.PlayerColony;
                Find.FactionManager.ofPlayer = value;

                worldComp.SetFaction(value);

                foreach (Map m in Find.Maps)
                    m.MpComp().SetFaction(value);
            }
        }

        public MultiplayerGame()
        {
            Toils_Ingest.cardinals = GenAdj.CardinalDirections.ToList();
            Toils_Ingest.diagonals = GenAdj.DiagonalDirections.ToList();
            GenAdj.adjRandomOrderList = null;
            CellFinder.mapEdgeCells = null;
            CellFinder.mapSingleEdgeCells = new List<IntVec3>[4];

            TradeSession.trader = null;
            TradeSession.playerNegotiator = null;
            TradeSession.deal = null;
            TradeSession.giftMode = false;

            DebugTools.curTool = null;
            PortraitsCache.Clear();
            RealTime.moteList.Clear();

            Room.nextRoomID = 1;
            RoomGroup.nextRoomGroupID = 1;
            Region.nextId = 1;
            ListerHaulables.groupCycleIndex = 0;

            ZoneColorUtility.nextGrowingZoneColorIndex = 0;
            ZoneColorUtility.nextStorageZoneColorIndex = 0;

            SetThingMakerSeed(1);

            foreach (var field in typeof(DebugSettings).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (!field.IsLiteral && field.FieldType == typeof(bool))
                    field.SetValue(null, default(bool));

            typeof(DebugSettings).TypeInitializer.Invoke(null, null);

            foreach (var resolver in DefDatabase<RuleDef>.AllDefs.SelectMany(r => r.resolvers))
                if (resolver is SymbolResolver_EdgeThing edgeThing)
                    edgeThing.randomRotations = new List<int>() { 0, 1, 2, 3 };

            typeof(SymbolResolver_SingleThing).TypeInitializer.Invoke(null, null);
        }

        public void SetThingMakerSeed(int seed)
        {
            foreach (var maker in CaptureThingSetMakers.captured)
            {
                if (maker is ThingSetMaker_Nutrition n)
                    n.nextSeed = seed;
                if (maker is ThingSetMaker_MarketValue m)
                    m.nextSeed = seed;
            }
        }
    }
}
