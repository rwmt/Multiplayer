using System.Collections.Generic;
using System.Diagnostics;
using Multiplayer.Client.Networking;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Steamworks;
using UnityEngine;
using Verse;
using Verse.Sound;

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
        public PacketLogWindow writerLog = new(true);
        public PacketLogWindow readerLog = new(false);
        public int myFactionId;
        public List<PlayerInfo> players = new();
        public GameDataSnapshot dataSnapshot;
        public LocationPings locationPings = new();
        public PlayerCursors playerCursors = new();
        public int autosaveCounter;
        public float? lastSaveAt;

        public bool replay;
        public int replayTimerStart = -1;
        public int replayTimerEnd = -1;
        public bool showTimeline;

        public bool desynced;
        // Caught exceptions in synced commands and ticks, see SimulationFailures
        public int simulationFailures;
        public string firstSimulationFailure;
        // Standalone: set by the server when this client's sync opinions are the ones compared against
        public bool isSyncReference;

        public List<CSteamID> pendingSteam = new();
        public List<CSteamID> knownUsers = new();

        public const int MaxMessages = 200;
        public List<ChatMsg> messages = new();
        public bool hasUnread;
        public bool ghostModeCheckbox;

        public Process arbiter;
        public bool ArbiterPlaying => players.Any(p => p.type == PlayerType.Arbiter && p.status == PlayerStatus.Playing);

        public IConnector connector;
        public bool isStandaloneServer;
        public float autosaveInterval;
        public AutosaveUnit autosaveUnit;
        public bool ConnectedToStandaloneServer => client != null && isStandaloneServer;

        public void Stop()
        {
            if (client != null)
            {
                client.Close(MpDisconnectReason.Internal);
                client.ChangeState(ConnectionStateEnum.Disconnected);
            }

            if (arbiter != null)
            {
                arbiter.TryKill();
                arbiter = null;
            }
        }

        public PlayerInfo GetPlayerInfo(int id) => players.FirstOrDefault(p => p.id == id);

        public void AddMsg(string msg, bool notify = true, bool rawMessage = false)
        {
            AddMsg(new ChatMsg_Text(msg, rawMessage), notify);
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

        public void Reconnect(string username)
        {
            Multiplayer.username = username;
            ClientUtil.TryConnectWithWindow(connector);
        }

        public void Connected()
        {
        }

        public void Disconnected(SessionDisconnectInfo info)
        {
            MpUI.ClearWindowStack();

            Find.WindowStack.Add(new DisconnectedWindow(info)
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

        // False while a (re)load is in progress: Loader.PostLoad then builds the command queue from dataSnapshot.Cmds,
        // which also contains every command received in the meantime.
        public bool cmdQueueReady = true;

        public void ScheduleCommand(ScheduledCommand cmd)
        {
            MpLog.Debug(cmd.ToString());
            dataSnapshot.Cmds.Add(cmd);

            if (!cmdQueueReady) return;

            // All commands (world and map) go to a single queue and run in arrival order, the same order the server
            // stores them in for joiners (#733)
            Multiplayer.AsyncWorldTime.Cmds.Enqueue(cmd);
        }

        public void Update()
        {
            locationPings.UpdatePing();
        }
    }
}
