using HarmonyLib;
using Ionic.Zlib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Multiplayer.Client.Util;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public enum JoiningState
    {
        Connected, Downloading
    }

    [HotSwappable]
    public class ClientJoiningState : ClientBaseState
    {
        public JoiningState subState = JoiningState.Connected;

        public ClientJoiningState(ConnectionBase connection) : base(connection)
        {
            connection.Send(Packets.Client_Username, MpVersion.Protocol, Multiplayer.username);

            ConnectionStatusListeners.TryNotifyAll_Connected();
        }

        [PacketHandler(Packets.Server_UsernameOk)]
        public void HandleUsernameOk(ByteReader data)
        {
            var writer = new ByteWriter();

            writer.WriteInt32(MultiplayerData.localDefInfos.Count);

            foreach (var kv in MultiplayerData.localDefInfos)
            {
                writer.WriteString(kv.Key);
                writer.WriteInt32(kv.Value.count);
                writer.WriteInt32(kv.Value.hash);
            }

            connection.SendFragmented(Packets.Client_JoinData, writer.ToArray());
        }

        [PacketHandler(Packets.Server_JoinData)]
        [IsFragmented]
        public void HandleJoinData(ByteReader data)
        {
            Multiplayer.session.gameName = data.ReadString();
            Multiplayer.session.playerId = data.ReadInt32();

            var remoteInfo = new RemoteData
            {
                remoteRwVersion = data.ReadString(),
                remoteMpVersion = data.ReadString(),
                remoteAddress = Multiplayer.session.address,
                remotePort = Multiplayer.session.port,
                remoteSteamHost = Multiplayer.session.steamHost
            };

            var defDiff = false;
            var defsData = new ByteReader(data.ReadPrefixedBytes());

            foreach (var local in MultiplayerData.localDefInfos)
            {
                var status = (DefCheckStatus)defsData.ReadByte();
                local.Value.status = status;

                if (status != DefCheckStatus.Ok)
                    defDiff = true;
            }

            JoinData.ReadServerData(data.ReadPrefixedBytes(), remoteInfo);

            OnMainThread.Schedule(Complete, 0.3f);

            void Complete()
            {
                if (JoinData.CompareToLocal(remoteInfo) && !defDiff)
                {
                    StartDownloading();
                    return;
                }

                if (defDiff)
                    Multiplayer.StopMultiplayer();

                var connectingWindow = Find.WindowStack.WindowOfType<BaseConnectingWindow>();
                MpUI.ClearWindowStack();

                Find.WindowStack.Add(new JoinDataWindow(remoteInfo){
                    connectAnywayDisabled = defDiff ? "MpMismatchDefsDiff".Translate() : null,
                    connectAnywayCallback = StartDownloading,
                    connectAnywayWindow = connectingWindow
                });

                void StartDownloading()
                {
                    connection.Send(Packets.Client_WorldRequest);
                    subState = JoiningState.Downloading;
                }
            }
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
            Session.cache.gameData = worldData;

            byte[] semiPersistentData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            Session.cache.semiPersistentData = semiPersistentData;

            List<int> mapsToLoad = new List<int>();

            int mapCmdsCount = data.ReadInt32();
            for (int i = 0; i < mapCmdsCount; i++)
            {
                int mapId = data.ReadInt32();

                int mapCmdsLen = data.ReadInt32();
                List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
                for (int j = 0; j < mapCmdsLen; j++)
                    mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

                Session.cache.mapCmds[mapId] = mapCmds;
            }

            int mapDataCount = data.ReadInt32();
            for (int i = 0; i < mapDataCount; i++)
            {
                int mapId = data.ReadInt32();
                byte[] rawMapData = data.ReadPrefixedBytes();

                byte[] mapData = GZipStream.UncompressBuffer(rawMapData);
                Session.cache.mapData[mapId] = mapData;
                mapsToLoad.Add(mapId);
            }

            Multiplayer.session.localCmdId = data.ReadInt32();

            TickPatch.tickUntil = tickUntil;

            TickPatch.SimulateTo(
                toTickUntil: true,
                onFinish: () => Multiplayer.Client.Send(Packets.Client_WorldReady),
                cancelButtonKey: "Quit",
                onCancel: GenScene.GoToMainMenu
            );

            ReloadGame(mapsToLoad, true, false);
        }

        private static XmlDocument GetGameDocument(List<int> mapsToLoad)
        {
            XmlDocument gameDoc = ScribeUtil.LoadDocument(Multiplayer.session.cache.gameData);
            XmlNode gameNode = gameDoc.DocumentElement["game"];

            foreach (int map in mapsToLoad)
            {
                using XmlReader reader = XmlReader.Create(new MemoryStream(Multiplayer.session.cache.mapData[map]));
                XmlNode mapNode = gameDoc.ReadNode(reader);
                gameNode["maps"].AppendChild(mapNode);

                if (gameNode["currentMapIndex"] == null)
                    gameNode.AddNode("currentMapIndex", map.ToString());
            }

            return gameDoc;
        }

        public static void ReloadGame(List<int> mapsToLoad, bool offMainThread, bool forceAsyncTime)
        {
            var gameDoc = GetGameDocument(mapsToLoad);

            LoadPatch.gameToLoad = new(gameDoc, Multiplayer.session.cache.semiPersistentData);
            TickPatch.replayTimeSpeed = TimeSpeed.Paused;

            if (offMainThread)
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    MemoryUtility.ClearAllMapsAndWorld();
                    Current.Game = new Game
                    {
                        InitData = new GameInitData
                        {
                            gameToLoad = "server"
                        }
                    };

                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        LongEventHandler.QueueLongEvent(() => PostLoad(forceAsyncTime), "MpSimulating", false, null);
                    });
                }, "Play", "MpLoading", true, null);
            }
            else
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    SaveLoad.LoadInMainThread(LoadPatch.gameToLoad);
                    PostLoad(forceAsyncTime);
                }, "MpLoading", false, null);
            }
        }

        private static void PostLoad(bool forceAsyncTime)
        {
            // If the client gets disconnected during loading
            if (Multiplayer.Client == null) return;

            Multiplayer.session.cache.cachedAtTime = TickPatch.Timer;
            Multiplayer.session.replayTimerStart = TickPatch.Timer;

            var factionData = Multiplayer.WorldComp.factionData.GetValueSafe(Multiplayer.session.myFactionId);
            if (factionData is { online: true })
                Multiplayer.RealPlayerFaction = Find.FactionManager.GetById(factionData.factionId);
            else
                //Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;
                throw new Exception("Currently not supported");

            // todo find a better way
            Multiplayer.game.myFactionLoading = null;

            if (forceAsyncTime)
                Multiplayer.game.gameComp.asyncTime = true;

            Multiplayer.WorldComp.cmds = new Queue<ScheduledCommand>(Multiplayer.session.cache.mapCmds.GetValueSafe(ScheduledCommand.Global) ?? new List<ScheduledCommand>());
            // Map cmds are added in MapAsyncTimeComp.FinalizeInit
        }
    }

}
