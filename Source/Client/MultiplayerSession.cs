using LiteNetLib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;
using Verse.Profile;
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
        public PacketLogWindow writerLog = new();
        public PacketLogWindow readerLog = new();
        public int myFactionId;
        public List<PlayerInfo> players = new();
        public GameDataSnapshot dataSnapshot = new();
        public CursorAndPing cursorAndPing = new();
        public int autosaveCounter;
        public float? lastSaveAt;
        public string desyncTracesFromHost;
        public List<ClientSyncOpinion> initialOpinions = new();

        public bool replay;
        public int replayTimerStart = -1;
        public int replayTimerEnd = -1;
        public List<ReplayEvent> events = new();

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
                client.State = ConnectionStateEnum.Disconnected;
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
            dataSnapshot.mapCmds.GetOrAddNew(cmd.mapId).Add(cmd);

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

        public static void DoAutosave()
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                SaveGameToFile(GetNextAutosaveFileName());
                Multiplayer.Client.Send(Packets.Client_Autosaving);
            }, "MpSaving", false, null);
        }

        private static string GetNextAutosaveFileName()
        {
            var autosavePrefix = "Autosave-";

            if (Multiplayer.settings.appendNameToAutosave)
                autosavePrefix += $"{GenFile.SanitizedFileName(Multiplayer.session.gameName)}-";

            return Enumerable
                .Range(1, Multiplayer.settings.autosaveSlots)
                .Select(i => $"{autosavePrefix}{i}")
                .OrderBy(s => new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{s}.zip")).LastWriteTime)
                .First();
        }

        public static void SaveGameToFile(string fileNameNoExtension)
        {
            Log.Message($"Multiplayer: saving to file {fileNameNoExtension}");

            try
            {
                new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{fileNameNoExtension}.zip")).Delete();
                Replay.ForSaving(fileNameNoExtension).WriteData(SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveGameData()));
                Messages.Message("MpGameSaved".Translate(fileNameNoExtension), MessageTypeDefOf.SilentInput, false);
                Multiplayer.session.lastSaveAt = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                Log.Error($"Exception saving multiplayer game: {e}");
                Messages.Message("MpGameSaveFailed".Translate(), MessageTypeDefOf.SilentInput, false);
            }
        }

        public static void DoRejoin()
        {
            Multiplayer.Client.State = ConnectionStateEnum.ClientJoining;
            Multiplayer.Client.Send(Packets.Client_WorldRequest);
            ((ClientJoiningState)Multiplayer.Client.StateObj).subState = JoiningState.Waiting;

            Multiplayer.session.desynced = false;

            Log.Message("Multiplayer: rejoining");

            // From GenScene.GoToMainMenu
            LongEventHandler.ClearQueuedEvents();
            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = null;

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    MpUI.ClearWindowStack();
                    Find.WindowStack.Add(new RejoiningWindow());
                });
            }, "Entry", "LoadingLongEvent", true, null, false);
        }
    }

    public struct SessionDisconnectInfo
    {
        public string titleTranslated;
        public string descTranslated;
        public string specialButtonTranslated;
        public Action specialButtonAction;
        public bool wideWindow;
    }

    public class GameDataSnapshot
    {
        public int cachedAtTime;
        public byte[] gameData;
        public byte[] semiPersistentData;
        public Dictionary<int, byte[]> mapData = new();

        // Global cmds are -1
        public Dictionary<int, List<ScheduledCommand>> mapCmds = new();
    }

    public class PlayerInfo
    {
        public static readonly Vector3 Invalid = new(-1, 0, -1);

        public int id;
        public string username;
        public int latency;
        public int ticksBehind;
        public bool simulating;
        public PlayerType type;
        public PlayerStatus status;
        public Color color;
        public int factionId;

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

        public Dictionary<int, float> selectedThings = new();

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
            var simulating = data.ReadBool();

            var color = new Color(data.ReadByte() / 255f, data.ReadByte() / 255f, data.ReadByte() / 255f);

            int factionId = data.ReadInt32();

            return new PlayerInfo(id, username, latency, type)
            {
                status = status,
                steamId = steamId,
                steamPersonaName = steamName,
                color = color,
                ticksBehind = ticksBehind,
                simulating = simulating,
                factionId = factionId
            };
        }
    }
}
