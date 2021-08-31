using LiteNetLib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using HarmonyLib;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;
using Verse.Sound;
using Random = System.Random;

namespace Multiplayer.Client
{
    public class MultiplayerSession : IConnectionStatusListener
    {
        public string gameName;
        public int playerId;

        public int localCmdId;
        public int remoteTickUntil;
        public int remoteCmdId;

        public ConnectionBase client;
        public NetManager netClient;
        public PacketLogWindow writerLog = new PacketLogWindow();
        public PacketLogWindow readerLog = new PacketLogWindow();
        public int myFactionId;
        public List<PlayerInfo> players = new List<PlayerInfo>();
        public SessionCache cache = new SessionCache();
        public CursorAndPing cursorAndPing = new();

        public bool replay;
        public int replayTimerStart = -1;
        public int replayTimerEnd = -1;
        public List<ReplayEvent> events = new List<ReplayEvent>();

        public bool desynced;
        public bool resyncing;

        public SessionDisconnectInfo disconnectInfo;

        public bool allowSteam;
        public List<CSteamID> pendingSteam = new List<CSteamID>();
        public List<CSteamID> knownUsers = new List<CSteamID>();
        public Thread steamNet;

        public const int MaxMessages = 200;
        public List<ChatMsg> messages = new List<ChatMsg>();
        public bool hasUnread;

        public MultiplayerServer localServer;
        public Thread serverThread;
        public ServerSettings localSettings;

        public Process arbiter;
        public bool ArbiterPlaying => players.Any(p => p.type == PlayerType.Arbiter && p.status == PlayerStatus.Playing);

        public string address;
        public int port;
        public CSteamID? steamHost;

        public void Stop()
        {
            if (client != null)
            {
                client.Close(MpDisconnectReason.Internal);
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
            for (int i = 0; i < players.Count; i++)
                if (players[i].id == id)
                    return players[i];
            return null;
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

        public void ProcessDisconnectPacket(MpDisconnectReason reason, byte[] data)
        {
            var reader = new ByteReader(data);
            string titleKey = null;
            string descKey = null;

            if (reason == MpDisconnectReason.GenericKeyed) titleKey = reader.ReadString();

            if (reason == MpDisconnectReason.Protocol)
            {
                titleKey = "MpWrongProtocol";

                string strVersion = reader.ReadString();
                int proto = reader.ReadInt32();

                disconnectInfo.titleTranslated = "MpWrongMultiplayerVersionInfo".Translate(strVersion, proto);
            }

            if (reason == MpDisconnectReason.NetFailed)
            {
                var netReason = (DisconnectReason)reader.ReadByte();

                disconnectInfo.titleTranslated =
                    netReason == DisconnectReason.ConnectionFailed ?
                    "MpConnectionFailed".Translate() :
                    "MpConnectionFailedWithInfo".Translate(netReason.ToString().CamelSpace().ToLowerInvariant());
            }

            if (reason == MpDisconnectReason.UsernameAlreadyOnline)
            {
                titleKey = "MpInvalidUsernameAlreadyPlaying";
                descKey = "MpChangeUsernameInfo";

                var newName = Multiplayer.username.Substring(0, Math.Min(Multiplayer.username.Length, MultiplayerServer.MaxUsernameLength - 3));
                newName += new Random().Next(1000);

                disconnectInfo.specialButtonTranslated = "MpConnectAs".Translate(newName);
                disconnectInfo.specialButtonAction = () => Reconnect(newName);
            }

            if (reason == MpDisconnectReason.UsernameLength) { titleKey = "MpInvalidUsernameLength"; descKey = "MpChangeUsernameInfo"; }
            if (reason == MpDisconnectReason.UsernameChars) { titleKey = "MpInvalidUsernameChars"; descKey = "MpChangeUsernameInfo"; }
            if (reason == MpDisconnectReason.ServerClosed) titleKey = "MpServerClosed";
            if (reason == MpDisconnectReason.ServerFull) titleKey = "MpServerFull";
            if (reason == MpDisconnectReason.Kick) titleKey = "MpKicked";
            if (reason == MpDisconnectReason.ServerPacketRead) descKey = "MpPacketErrorRemote";

            disconnectInfo.titleTranslated ??= titleKey?.Translate();
            disconnectInfo.descTranslated ??= descKey?.Translate();
        }

        public void Reconnect(string username)
        {
            Multiplayer.username = username;

            if (steamHost is { } host)
                ClientUtil.TrySteamConnectWithWindow(host);
            else
                ClientUtil.TryConnectWithWindow(address, port);
        }

        public void Connected()
        {
        }

        public void Disconnected()
        {
            MpUI.ClearWindowStack();

            Find.WindowStack.Add(new DisconnectedWindow(disconnectInfo)
            {
                returnToServerBrowser = Multiplayer.Client?.State != ConnectionStateEnum.ClientPlaying
            });
        }

        public void ReapplyPrefs()
        {
            Application.runInBackground = true;
        }

        public void ProcessTimeControl()
        {
            if (localCmdId >= remoteCmdId)
                TickPatch.tickUntil = remoteTickUntil;
        }

        public void ScheduleCommand(ScheduledCommand cmd)
        {
            MpLog.Log($"Cmd: {cmd.type}, faction: {cmd.factionId}, map: {cmd.mapId}, ticks: {cmd.ticks}");
            cache.mapCmds.GetOrAddNew(cmd.mapId).Add(cmd);

            if (Current.ProgramState != ProgramState.Playing) return;

            if (cmd.mapId == ScheduledCommand.Global)
                Multiplayer.WorldComp.cmds.Enqueue(cmd);
            else
                cmd.GetMap()?.AsyncTime().cmds.Enqueue(cmd);
        }

        public void Update()
        {
            cursorAndPing.UpdatePing();
        }
    }

    public struct SessionDisconnectInfo
    {
        public string titleTranslated;
        public string descTranslated;
        public string specialButtonTranslated;
        public Action specialButtonAction;
    }

    public class SessionCache
    {
        public int cachedAtTime;
        public byte[] gameData;
        public byte[] semiPersistentData;
        public Dictionary<int, byte[]> mapData = new Dictionary<int, byte[]>();

        // Global cmds are -1
        public Dictionary<int, List<ScheduledCommand>> mapCmds = new Dictionary<int, List<ScheduledCommand>>();
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
        public Color color;

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

            var color = new Color(data.ReadByte() / 255f, data.ReadByte() / 255f, data.ReadByte() / 255f);

            return new PlayerInfo(id, username, latency, type)
            {
                status = status,
                steamId = steamId,
                steamPersonaName = steamName,
                color = color,
                ticksBehind = ticksBehind
            };
        }
    }
}
