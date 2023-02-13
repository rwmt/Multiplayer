using HarmonyLib;
using Ionic.Zlib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public enum JoiningState
    {
        Connected, Waiting, Downloading
    }


    public class ClientJoiningState : ClientBaseState
    {
        public JoiningState subState = JoiningState.Connected;

        public ClientJoiningState(ConnectionBase connection) : base(connection)
        {
        }

        public override void StartState()
        {
            connection.Send(Packets.Client_Protocol, MpVersion.Protocol);
            ConnectionStatusListeners.TryNotifyAll_Connected();
        }

        [PacketHandler(Packets.Server_ProtocolOk)]
        public void HandleProtocolOk(ByteReader data)
        {
            bool hasPassword = data.ReadBool();

            if (hasPassword)
            {
                // Delay showing the window for better UX
                OnMainThread.Schedule(() => Find.WindowStack.Add(new GamePasswordWindow
                {
                    returnToServerBrowser = Find.WindowStack.WindowOfType<BaseConnectingWindow>().returnToServerBrowser
                }), 0.3f);
            }
            else
            {
                connection.Send(Packets.Client_Username, Multiplayer.username);
            }
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

            // Delay showing the window for better UX
            OnMainThread.Schedule(Complete, 0.3f);

            void Complete()
            {
                if (JoinData.CompareToLocal(remoteInfo) && !defDiff)
                {
                    StartDownloading();
                    return;
                }

                if (defDiff)
                    Multiplayer.StopMultiplayerAndClearAllWindows();

                var defDiffStr = "\n\n" + MultiplayerData.localDefInfos
                    .Where(kv => kv.Value.status != DefCheckStatus.Ok)
                    .Take(10)
                    .Join(kv => $"{kv.Key}: {kv.Value.status}", "\n");

                Find.WindowStack.Add(new JoinDataWindow(remoteInfo){
                    connectAnywayDisabled = defDiff ? "MpMismatchDefsDiff".Translate() + defDiffStr : null,
                    connectAnywayCallback = StartDownloading
                });

                void StartDownloading()
                {
                    connection.Send(Packets.Client_WorldRequest);
                    subState = JoiningState.Waiting;
                }
            }
        }

        [PacketHandler(Packets.Server_WorldDataStart)]
        public void HandleWorldDataStart(ByteReader data)
        {
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

            var dataSnapshot = new GameDataSnapshot();

            byte[] worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            dataSnapshot.gameData = worldData;

            byte[] semiPersistentData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            dataSnapshot.semiPersistentData = semiPersistentData;

            List<int> mapsToLoad = new List<int>();

            int mapCmdsCount = data.ReadInt32();
            for (int i = 0; i < mapCmdsCount; i++)
            {
                int mapId = data.ReadInt32();

                int mapCmdsLen = data.ReadInt32();
                List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
                for (int j = 0; j < mapCmdsLen; j++)
                    mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

                dataSnapshot.mapCmds[mapId] = mapCmds;
            }

            int mapDataCount = data.ReadInt32();
            for (int i = 0; i < mapDataCount; i++)
            {
                int mapId = data.ReadInt32();
                byte[] rawMapData = data.ReadPrefixedBytes();

                byte[] mapData = GZipStream.UncompressBuffer(rawMapData);
                dataSnapshot.mapData[mapId] = mapData;
                mapsToLoad.Add(mapId);
            }

            Session.dataSnapshot = dataSnapshot;
            Multiplayer.session.receivedCmds = data.ReadInt32();
            TickPatch.serverFrozen = data.ReadBool();
            TickPatch.tickUntil = tickUntil;

            int syncInfos = data.ReadInt32();
            for (int i = 0; i < syncInfos; i++)
                Session.initialOpinions.Add(ClientSyncOpinion.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            Log.Message(syncInfos > 0
                ? $"Initial sync opinions: {Session.initialOpinions.First().startTick}...{Session.initialOpinions.Last().startTick}"
                : "No initial sync opinions");

            TickPatch.SetSimulation(
                toTickUntil: true,
                onFinish: () => Multiplayer.Client.Send(Packets.Client_WorldReady),
                cancelButtonKey: "Quit",
                onCancel: GenScene.GoToMainMenu // Calls StopMultiplayer through a patch
            );

            ReloadGame(mapsToLoad, true, false);
        }

        private static XmlDocument GetGameDocument(List<int> mapsToLoad)
        {
            XmlDocument gameDoc = ScribeUtil.LoadDocument(Multiplayer.session.dataSnapshot.gameData);
            XmlNode gameNode = gameDoc.DocumentElement["game"];

            foreach (int map in mapsToLoad)
            {
                using XmlReader reader = XmlReader.Create(new MemoryStream(Multiplayer.session.dataSnapshot.mapData[map]));
                XmlNode mapNode = gameDoc.ReadNode(reader);
                gameNode["maps"].AppendChild(mapNode);

                if (gameNode["currentMapIndex"] == null)
                    gameNode.AddNode("currentMapIndex", map.ToString());
            }

            return gameDoc;
        }

        public static void ReloadGame(List<int> mapsToLoad, bool changeScene, bool forceAsyncTime)
        {
            var gameDoc = GetGameDocument(mapsToLoad);

            LoadPatch.gameToLoad = new(gameDoc, Multiplayer.session.dataSnapshot.semiPersistentData);
            TickPatch.replayTimeSpeed = TimeSpeed.Paused;

            if (changeScene)
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

            Multiplayer.session.dataSnapshot.cachedAtTime = TickPatch.Timer;
            Multiplayer.session.replayTimerStart = TickPatch.Timer;

            Multiplayer.game.ChangeRealPlayerFaction(Find.FactionManager.GetById(Multiplayer.session.myFactionId));
            Multiplayer.game.myFactionLoading = null;

            if (forceAsyncTime)
                Multiplayer.game.gameComp.asyncTime = true;

            Multiplayer.WorldComp.cmds = new Queue<ScheduledCommand>(Multiplayer.session.dataSnapshot.mapCmds.GetValueSafe(ScheduledCommand.Global) ?? new List<ScheduledCommand>());
            // Map cmds are added in MapAsyncTimeComp.FinalizeInit
        }
    }

}
