using HarmonyLib;
using Ionic.Zlib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using RestSharp;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public class ClientJoiningState : MpConnectionState
    {
        public JoiningState subState = JoiningState.Connected;

        public ClientJoiningState(IConnection connection) : base(connection)
        {
            SendJoinData();
            ConnectionStatusListeners.TryNotifyAll_Connected();
        }

        public void SendJoinData()
        {
            var data = new ByteWriter();

            data.WriteInt32(MpVersion.Protocol);
            data.WriteInt32(Multiplayer.localDefInfos.Count);

            foreach (var kv in Multiplayer.localDefInfos)
            {
                data.WriteString(kv.Key);
                data.WriteInt32(kv.Value.count);
                data.WriteInt32(kv.Value.hash);
            }

            connection.SendFragmented(Packets.Client_JoinData, data.ToArray());
        }

        [PacketHandler(Packets.Server_JoinData)]
        [IsFragmented]
        public void HandleJoinData(ByteReader data)
        {
            Multiplayer.session.gameName = data.ReadString();
            Multiplayer.session.playerId = data.ReadInt32();

            var remoteInfo = new RemoteData();

            remoteInfo.remoteRwVersion = data.ReadString();
            remoteInfo.defInfo = Multiplayer.localDefInfos; // by ref, is that fine?

            var defDiff = false;
            var defsData = new ByteReader(data.ReadPrefixedBytes());

            foreach (var local in remoteInfo.defInfo)
            {
                var status = (DefCheckStatus)defsData.ReadByte();
                local.Value.status = status;

                if (status != DefCheckStatus.OK)
                    defDiff = true;
            }

            JoinData.ReadServerData(data.ReadPrefixedBytes(), remoteInfo);

            if (false) // for testing
            if (!JoinData.DataEqual(remoteInfo) || defDiff)
            {
                if (defDiff)
                    OnMainThread.StopMultiplayer();

                MpUtil.ClearWindowStack();
                Find.WindowStack.Add(new JoinDataWindow(remoteInfo));

                return;
            }

            connection.Send(Packets.Client_Username, Multiplayer.username);

            subState = JoiningState.Downloading;
        }

        [PacketHandler(Packets.Server_WorldData)]
        [IsFragmented]
        public void HandleWorldData(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ClientPlaying;
            Log.Message("Game data size: " + data.Length);

            int factionId = data.ReadInt32();
            Multiplayer.session.myFactionId = factionId;

            int tickUntil = data.ReadInt32();

            byte[] worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.cachedGameData = worldData;

            List<int> mapsToLoad = new List<int>();

            int mapCmdsCount = data.ReadInt32();
            for (int i = 0; i < mapCmdsCount; i++)
            {
                int mapId = data.ReadInt32();

                int mapCmdsLen = data.ReadInt32();
                List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
                for (int j = 0; j < mapCmdsLen; j++)
                    mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

                OnMainThread.cachedMapCmds[mapId] = mapCmds;
            }

            int mapDataCount = data.ReadInt32();
            for (int i = 0; i < mapDataCount; i++)
            {
                int mapId = data.ReadInt32();
                byte[] rawMapData = data.ReadPrefixedBytes();

                byte[] mapData = GZipStream.UncompressBuffer(rawMapData);
                OnMainThread.cachedMapData[mapId] = mapData;
                mapsToLoad.Add(mapId);
            }

            Multiplayer.session.localCmdId = data.ReadInt32();

            TickPatch.tickUntil = tickUntil;

            TickPatch.SkipTo(
                toTickUntil: true,
                onFinish: () => Multiplayer.Client.Send(Packets.Client_WorldReady),
                cancelButtonKey: "Quit",
                onCancel: GenScene.GoToMainMenu
            );

            ReloadGame(mapsToLoad);
        }

        private static XmlDocument GetGameDocument(List<int> mapsToLoad)
        {
            XmlDocument gameDoc = ScribeUtil.LoadDocument(OnMainThread.cachedGameData);
            XmlNode gameNode = gameDoc.DocumentElement["game"];

            foreach (int map in mapsToLoad)
            {
                using (XmlReader reader = XmlReader.Create(new MemoryStream(OnMainThread.cachedMapData[map])))
                {
                    XmlNode mapNode = gameDoc.ReadNode(reader);
                    gameNode["maps"].AppendChild(mapNode);

                    if (gameNode["currentMapIndex"] == null)
                        gameNode.AddNode("currentMapIndex", map.ToString());
                }
            }

            return gameDoc;
        }

        public static void ReloadGame(List<int> mapsToLoad, bool async = true)
        {
            LoadPatch.gameToLoad = GetGameDocument(mapsToLoad);
            TickPatch.replayTimeSpeed = TimeSpeed.Paused;

            if (async)
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    MemoryUtility.ClearAllMapsAndWorld();
                    Current.Game = new Game();
                    Current.Game.InitData = new GameInitData();
                    Current.Game.InitData.gameToLoad = "server";

                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        LongEventHandler.QueueLongEvent(() => PostLoad(), "MpSimulating", false, null);
                    });
                }, "Play", "MpLoading", true, null);
            }
            else
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    SaveLoad.LoadInMainThread(LoadPatch.gameToLoad);
                    PostLoad();
                }, "MpLoading", false, null);
            }
        }

        private static void PostLoad()
        {
            // If the client gets disconnected during loading
            if (Multiplayer.Client == null) return;

            OnMainThread.cachedAtTime = TickPatch.Timer;
            Multiplayer.session.replayTimerStart = TickPatch.Timer;

            var factionData = Multiplayer.WorldComp.factionData.GetValueSafe(Multiplayer.session.myFactionId);
            if (factionData != null && factionData.online)
                Multiplayer.RealPlayerFaction = Find.FactionManager.GetById(factionData.factionId);
            else
                //Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;
                throw new Exception("Currently not supported");

            // todo find a better way
            Multiplayer.game.myFactionLoading = null;

            Multiplayer.WorldComp.cmds = new Queue<ScheduledCommand>(OnMainThread.cachedMapCmds.GetValueSafe(ScheduledCommand.Global) ?? new List<ScheduledCommand>());
            // Map cmds are added in MapAsyncTimeComp.FinalizeInit
        }
    }

    public enum JoiningState
    {
        Connected, Downloading
    }

    public class ClientPlayingState : MpConnectionState
    {
        public ClientPlayingState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.Server_TimeControl)]
        public void HandleTimeControl(ByteReader data)
        {
            int tickUntil = data.ReadInt32();
            int cmdId = data.ReadInt32();

            if (TickPatch.tickUntil >= tickUntil) return;

            Multiplayer.session.remoteTickUntil = tickUntil;
            Multiplayer.session.remoteCmdId = cmdId;
            Multiplayer.session.ProcessTimeControl();
        }

        [PacketHandler(Packets.Server_KeepAlive)]
        public void HandleKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            int ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

            connection.Send(Packets.Client_KeepAlive, id, (ticksBehind << 1) | (TickPatch.Skipping ? 1 : 0));
        }

        [PacketHandler(Packets.Server_Command)]
        public void HandleCommand(ByteReader data)
        {
            ScheduledCommand cmd = ScheduledCommand.Deserialize(data);
            cmd.issuedBySelf = data.ReadBool();
            OnMainThread.ScheduleCommand(cmd);

            Multiplayer.session.localCmdId++;
            Multiplayer.session.ProcessTimeControl();
        }

        [PacketHandler(Packets.Server_PlayerList)]
        public void HandlePlayerList(ByteReader data)
        {
            var action = (PlayerListAction)data.ReadByte();

            if (action == PlayerListAction.Add)
            {
                var info = PlayerInfo.Read(data);
                if (!Multiplayer.session.players.Contains(info))
                    Multiplayer.session.players.Add(info);
            }
            else if (action == PlayerListAction.Remove)
            {
                int id = data.ReadInt32();
                Multiplayer.session.players.RemoveAll(p => p.id == id);
            }
            else if (action == PlayerListAction.List)
            {
                int count = data.ReadInt32();

                Multiplayer.session.players.Clear();
                for (int i = 0; i < count; i++)
                    Multiplayer.session.players.Add(PlayerInfo.Read(data));
            }
            else if (action == PlayerListAction.Latencies)
            {
                int count = data.ReadInt32();

                for (int i = 0; i < count; i++)
                {
                    var player = Multiplayer.session.players[i];
                    player.latency = data.ReadInt32();
                    player.ticksBehind = data.ReadInt32();
                }
            }
            else if (action == PlayerListAction.Status)
            {
                var id = data.ReadInt32();
                var status = (PlayerStatus)data.ReadByte();
                var player = Multiplayer.session.GetPlayerInfo(id);

                if (player != null)
                    player.status = status;
            }
        }

        [PacketHandler(Packets.Server_Chat)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            Multiplayer.session.AddMsg(msg);
        }

        [PacketHandler(Packets.Server_Cursor)]
        public void HandleCursor(ByteReader data)
        {
            int playerId = data.ReadInt32();
            var player = Multiplayer.session.GetPlayerInfo(playerId);
            if (player == null) return;

            byte seq = data.ReadByte();
            if (seq < player.cursorSeq && player.cursorSeq - seq < 128) return;

            byte map = data.ReadByte();
            player.map = map;

            if (map == byte.MaxValue) return;

            byte icon = data.ReadByte();
            float x = data.ReadShort() / 10f;
            float z = data.ReadShort() / 10f;

            player.cursorSeq = seq;
            player.lastCursor = player.cursor;
            player.lastDelta = Multiplayer.Clock.ElapsedMillisDouble() - player.updatedAt;
            player.cursor = new Vector3(x, 0, z);
            player.updatedAt = Multiplayer.Clock.ElapsedMillisDouble();
            player.cursorIcon = icon;

            short dragXRaw = data.ReadShort();
            if (dragXRaw != -1)
            {
                float dragX = dragXRaw / 10f;
                float dragZ = data.ReadShort() / 10f;

                player.dragStart = new Vector3(dragX, 0, dragZ);
            }
            else
            {
                player.dragStart = PlayerInfo.Invalid;
            }
        }

        [PacketHandler(Packets.Server_Selected)]
        public void HandleSelected(ByteReader data)
        {
            int playerId = data.ReadInt32();
            var player = Multiplayer.session.GetPlayerInfo(playerId);
            if (player == null) return;

            bool reset = data.ReadBool();

            if (reset)
                player.selectedThings.Clear();

            int[] add = data.ReadPrefixedInts();
            for (int i = 0; i < add.Length; i++)
                player.selectedThings[add[i]] = Time.realtimeSinceStartup;

            int[] remove = data.ReadPrefixedInts();
            for (int i = 0; i < remove.Length; i++)
                player.selectedThings.Remove(remove[i]);
        }

        [PacketHandler(Packets.Server_MapResponse)]
        public void HandleMapResponse(ByteReader data)
        {
            int mapId = data.ReadInt32();

            int mapCmdsLen = data.ReadInt32();
            List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (int j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            OnMainThread.cachedMapCmds[mapId] = mapCmds;

            byte[] mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.cachedMapData[mapId] = mapData;

            //ClientJoiningState.ReloadGame(TickPatch.tickUntil, Find.Maps.Select(m => m.uniqueID).Concat(mapId).ToList());
            // todo Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
        }

        [PacketHandler(Packets.Server_Notification)]
        public void HandleNotification(ByteReader data)
        {
            string key = data.ReadString();
            string[] args = data.ReadPrefixedStrings();

            Messages.Message(key.Translate(Array.ConvertAll(args, s => (NamedArgument)s)), MessageTypeDefOf.SilentInput, false);
        }

        [PacketHandler(Packets.Server_SyncInfo)]
        [IsFragmented]
        public void HandleDesyncCheck(ByteReader data)
        {
            Multiplayer.game?.sync.AddClientOpinionAndCheckDesync(ClientSyncOpinion.Deserialize(data));
        }

        [PacketHandler(Packets.Server_Pause)]
        public void HandlePause(ByteReader data)
        {
            bool pause = data.ReadBool();
            // This packet doesn't get processed in time during a synchronous long event
        }

        [PacketHandler(Packets.Server_Debug)]
        public void HandleDebug(ByteReader data)
        {
            int tick = data.ReadInt32();
            int diffAt = data.ReadInt32();
            var info = Multiplayer.game.sync.knownClientOpinions.FirstOrDefault(b => b.startTick == tick);
            var side = MultiplayerMod.arbiterInstance ? "Arbiter" : "Host";

            Log.Message($"{info?.desyncStackTraces.Count} {side} traces {diffAt} / {Multiplayer.game.sync.knownClientOpinions.Select(o => o.startTick).Join()}");

            File.WriteAllText(
                MpUtil.RwDataFile($"MP_{side}Traces.txt"),
                info?.GetFormattedStackTracesForRange(diffAt) ?? "null"
            );
        }
    }

    public class ClientSteamState : MpConnectionState
    {
        public ClientSteamState(IConnection connection) : base(connection)
        {
            var steamConn = connection as SteamBaseConn;

            // The flag byte is: joinPacket | reliable | hasChannel
            steamConn.SendRawSteam(ByteWriter.GetBytes((byte)0b111, steamConn.recvChannel), true);
        }

        [PacketHandler(Packets.Server_SteamAccept)]
        public void HandleSteamAccept(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ClientJoining;
        }
    }

    public interface IConnectionStatusListener
    {
        void Connected();
        void Disconnected();
    }

    public static class ConnectionStatusListeners
    {
        private static IEnumerable<IConnectionStatusListener> All
        {
            get
            {
                if (Find.WindowStack != null)
                    foreach (Window window in Find.WindowStack.Windows.ToList())
                        if (window is IConnectionStatusListener listener)
                            yield return listener;

                if (Multiplayer.Client?.StateObj is IConnectionStatusListener state)
                    yield return state;

                if (Multiplayer.session != null)
                    yield return Multiplayer.session;
            }
        }

        public static void TryNotifyAll_Connected()
        {
            foreach (var listener in All)
            {
                try
                {
                    listener.Connected();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }
        }

        public static void TryNotifyAll_Disconnected()
        {
            foreach (var listener in All)
            {
                try
                {
                    listener.Disconnected();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }
        }
    }

}
