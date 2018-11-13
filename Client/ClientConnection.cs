using Harmony;
using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public class ClientJoiningState : MpConnectionState
    {
        public ClientJoiningState(IConnection connection) : base(connection)
        {
            connection.Send(Packets.Client_Username, Multiplayer.username);
            connection.Send(Packets.Client_RequestWorld);
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

            TickPatch.tickUntil = tickUntil;

            TickPatch.skipToTickUntil = true;
            TickPatch.afterSkip = () => Multiplayer.Client.Send(Packets.Client_WorldLoaded);

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
                    Multiplayer.LoadInMainThread(LoadPatch.gameToLoad);
                    PostLoad();
                }, "MpLoading", false, null);
            }
        }

        private static void PostLoad()
        {
            OnMainThread.cachedAtTime = TickPatch.Timer;

            FactionWorldData factionData = Multiplayer.WorldComp.factionData.GetValueSafe(Multiplayer.session.myFactionId);
            if (factionData != null && factionData.online)
                Multiplayer.RealPlayerFaction = Find.FactionManager.GetById(factionData.factionId);
            else
                Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;

            Multiplayer.WorldComp.cmds = new Queue<ScheduledCommand>(OnMainThread.cachedMapCmds.GetValueSafe(ScheduledCommand.Global) ?? new List<ScheduledCommand>());
            // Map cmds are added in MapAsyncTimeComp.FinalizeInit
        }

        [PacketHandler(Packets.Server_DisconnectReason)]
        public void HandleDisconnectReason(ByteReader data)
        {
            string reason = data.ReadString();
            Multiplayer.session.disconnectServerReason = reason;
        }
    }

    [HotSwappable]
    public class ClientPlayingState : MpConnectionState, IConnectionStatusListener
    {
        public ClientPlayingState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.Server_TimeControl)]
        public void HandleTimeControl(ByteReader data)
        {
            int tickUntil = data.ReadInt32();
            TickPatch.tickUntil = tickUntil;
        }

        [PacketHandler(Packets.Server_KeepAlive)]
        public void HandleKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            connection.Send(Packets.Client_KeepAlive, id);
        }

        [PacketHandler(Packets.Server_Command)]
        public void HandleCommand(ByteReader data)
        {
            ScheduledCommand cmd = ScheduledCommand.Deserialize(data);
            cmd.issuedBySelf = data.ReadBool();
            OnMainThread.ScheduleCommand(cmd);
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
                int[] latencies = data.ReadPrefixedInts();

                for (int i = 0; i < Multiplayer.session.players.Count; i++)
                    Multiplayer.session.players[i].latency = latencies[i];
            }
        }

        [PacketHandler(Packets.Server_Chat)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            Multiplayer.Chat.AddMsg(msg);
        }

        [PacketHandler(Packets.Server_Cursor)]
        public void HandleCursor(ByteReader data)
        {
            int playerId = data.ReadInt32();
            var player = Multiplayer.session.players.Find(p => p.id == playerId);
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
            player.lastDelta = Multiplayer.Time.ElapsedMillisDouble() - player.updatedAt;
            player.cursor = new Vector3(x, 0, z);
            player.updatedAt = Multiplayer.Time.ElapsedMillisDouble();
            player.cursorIcon = icon;
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
            string msg = data.ReadString();
            string[] keys = data.ReadPrefixedStrings();

            Messages.Message(msg, MessageTypeDefOf.SilentInput, false);
        }

        [PacketHandler(Packets.Server_DisconnectReason)]
        public void HandleDisconnectReason(ByteReader data)
        {
            string reason = data.ReadString();
            Multiplayer.session.disconnectServerReason = reason;
        }

        [PacketHandler(Packets.Server_DesyncCheck)]
        public void HandleDesyncCheck(ByteReader data)
        {
            Multiplayer.game?.sync.Add(SyncInfo.Deserialize(data));
        }

        public void Connected()
        {
        }

        public void Disconnected()
        {
            Find.WindowStack.Add(new DisconnectedWindow(Multiplayer.session.disconnectServerReason ?? Multiplayer.session.disconnectNetReason));
        }
    }

    public class ClientSteamState : MpConnectionState
    {
        public ClientSteamState(IConnection connection) : base(connection)
        {
            connection.Send(Packets.Client_SteamRequest);
        }

        [PacketHandler(Packets.Server_SteamAccept)]
        public void HandleSteamAccept(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ClientJoining;
        }

        [PacketHandler(Packets.Server_DisconnectReason)]
        public void HandleDisconnectReason(ByteReader data)
        {
            string reason = data.ReadString();
            Multiplayer.session.disconnectServerReason = reason;
        }
    }

    public class DisconnectedWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        private string reason;

        public DisconnectedWindow(string reason)
        {
            this.reason = reason;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float ButtonWidth = 140f;
            const float ButtonHeight = 40f;

            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.MiddleCenter;
            Rect labelRect = inRect;
            labelRect.yMax -= ButtonHeight;
            Widgets.Label(labelRect, reason);
            Text.Anchor = TextAnchor.UpperLeft;

            Rect buttonRect = new Rect((inRect.width - ButtonWidth) / 2f, inRect.height - ButtonHeight - 10f, ButtonWidth, ButtonHeight);
            if (Widgets.ButtonText(buttonRect, "Quit to main menu", true, false, true))
            {
                GenScene.GoToMainMenu();
            }
        }
    }

    public interface IConnectionStatusListener
    {
        void Connected();
        void Disconnected();
    }

    public static class ConnectionStatusListeners
    {
        public static IEnumerable<IConnectionStatusListener> All
        {
            get
            {
                if (Find.WindowStack != null)
                    foreach (Window window in Find.WindowStack.Windows)
                        if (window is IConnectionStatusListener listener)
                            yield return listener;

                if (Multiplayer.Client?.StateObj is IConnectionStatusListener state)
                    yield return state;
            }
        }
    }

}
