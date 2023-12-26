using LiteNetLib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public int receivedCmds;
        public int remoteTickUntil;
        public int remoteSentCmds;

        public ConnectionBase client;
        public NetManager netClient;
        public PacketLogWindow writerLog = new(true);
        public PacketLogWindow readerLog = new(false);
        public int myFactionId;
        public List<PlayerInfo> players = new();
        public GameDataSnapshot dataSnapshot;
        public LocationPings locationPings = new();
        public PlayerCursors playerCursors = new();
        public int autosaveCounter;
        public float? lastSaveAt;
        public string desyncTracesFromHost;
        public List<ClientSyncOpinion> initialOpinions = new();

        public bool replay;
        public int replayTimerStart = -1;
        public int replayTimerEnd = -1;
        public bool showTimeline;

        public bool desynced;

        public SessionDisconnectInfo disconnectInfo;

        public List<CSteamID> pendingSteam = new();
        public List<CSteamID> knownUsers = new();

        public const int MaxMessages = 200;
        public List<ChatMsg> messages = new();
        public bool hasUnread;
        public bool ghostModeCheckbox;

        public ServerSettings localServerSettings;

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
                client.ChangeState(ConnectionStateEnum.Disconnected);
            }

            netClient?.Stop();

            if (arbiter != null)
            {
                arbiter.TryKill();
                arbiter = null;
            }
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
            SoundDefOf.PageChange.PlayOneShotOnCamera();
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

                disconnectInfo.wideWindow = true;
                disconnectInfo.descTranslated = "MpWrongMultiplayerVersionDesc".Translate(strVersion, proto, MpVersion.Version, MpVersion.Protocol);

                if (proto < MpVersion.Protocol)
                    disconnectInfo.descTranslated += "\n" + "MpWrongVersionUpdateInfoHost".Translate();
                else
                    disconnectInfo.descTranslated += "\n" + "MpWrongVersionUpdateInfo".Translate();
            }

            if (reason == MpDisconnectReason.ConnectingFailed)
            {
                var netReason = (DisconnectReason)reader.ReadByte();

                disconnectInfo.titleTranslated =
                    netReason == DisconnectReason.ConnectionFailed ?
                    "MpConnectionFailed".Translate() :
                    "MpConnectionFailedWithInfo".Translate(netReason.ToString().CamelSpace().ToLowerInvariant());
            }

            if (reason == MpDisconnectReason.NetFailed)
            {
                var netReason = (DisconnectReason)reader.ReadByte();

                disconnectInfo.titleTranslated =
                    "MpDisconnectedWithInfo".Translate(netReason.ToString().CamelSpace().ToLowerInvariant());
            }

            if (reason == MpDisconnectReason.UsernameAlreadyOnline)
            {
                titleKey = "MpInvalidUsernameAlreadyPlaying";
                descKey = "MpChangeUsernameInfo";

                var newName = Multiplayer.username.Substring(0, Math.Min(Multiplayer.username.Length, MultiplayerServer.MaxUsernameLength - 3));
                newName += new Random().Next(1000);

                disconnectInfo.specialButtonTranslated = "MpConnectAsUsername".Translate(newName);
                disconnectInfo.specialButtonAction = () => Reconnect(newName);
            }

            if (reason == MpDisconnectReason.UsernameLength) { titleKey = "MpInvalidUsernameLength"; descKey = "MpChangeUsernameInfo"; }
            if (reason == MpDisconnectReason.UsernameChars) { titleKey = "MpInvalidUsernameChars"; descKey = "MpChangeUsernameInfo"; }
            if (reason == MpDisconnectReason.ServerClosed) titleKey = "MpServerClosed";
            if (reason == MpDisconnectReason.ServerFull) titleKey = "MpServerFull";
            if (reason == MpDisconnectReason.ServerStarting) titleKey = "MpDisconnectServerStarting";
            if (reason == MpDisconnectReason.Kick) titleKey = "MpKicked";
            if (reason == MpDisconnectReason.ServerPacketRead) descKey = "MpPacketErrorRemote";
            if (reason == MpDisconnectReason.BadGamePassword) descKey = "MpBadGamePassword";

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
            if (receivedCmds >= remoteSentCmds)
                TickPatch.tickUntil = remoteTickUntil;
        }

        public void ScheduleCommand(ScheduledCommand cmd)
        {
            MpLog.Debug(cmd.ToString());
            dataSnapshot.MapCmds.GetOrAddNew(cmd.mapId).Add(cmd);

            if (Current.ProgramState != ProgramState.Playing) return;

            if (cmd.mapId == ScheduledCommand.Global)
                Multiplayer.AsyncWorldTime.cmds.Enqueue(cmd);
            else
                cmd.GetMap()?.AsyncTime().cmds.Enqueue(cmd);
        }

        public void Update()
        {
            locationPings.UpdatePing();
        }
    }
}
