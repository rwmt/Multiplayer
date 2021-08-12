using HarmonyLib;
using Ionic.Zlib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public enum JoiningState
    {
        Connected, Downloading
    }

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

            byte[] semiPersistentData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.cachedSemiPersistent = semiPersistentData;

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
            var gameDoc = GetGameDocument(mapsToLoad);

            LoadPatch.gameToLoad = new(gameDoc, OnMainThread.cachedSemiPersistent);
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

}
