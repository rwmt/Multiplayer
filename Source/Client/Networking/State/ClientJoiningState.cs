using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public class ClientJoiningState : ClientBaseState
    {
        public ClientJoiningState(ConnectionBase connection) : base(connection)
        {
        }

        public override void StartState()
        {
            connection.Send(ClientProtocolPacket.Current());
            ConnectionStatusListeners.TryNotifyAll_Connected();
        }

        [TypedPacketHandler]
        public void HandleProtocolOk(ServerProtocolOkPacket packet)
        {
            if (packet.hasPassword)
            {
                // Delay showing the window for better UX
                OnMainThread.Schedule(() => Find.WindowStack.Add(new GamePasswordWindow
                {
                    returnToServerBrowser = Find.WindowStack.WindowOfType<BaseConnectingWindow>().returnToServerBrowser
                }), 0.3f);
            }
            else
            {
                connection.Send(new ClientUsernamePacket(Multiplayer.username));
            }
        }

        [PacketHandler(Packets.Server_InitDataRequest)]
        public void HandleInitDataRequest(ByteReader data)
        {
            var includeConfigs = data.ReadBool();
            connection.SendFragmented(Packets.Client_InitData, PackInitData(includeConfigs));
        }

        public static byte[] PackInitData(bool includeConfigs)
        {
            return ServerInitData.Serialize(new ServerInitData(
                JoinData.WriteServerData(includeConfigs),
                VersionControl.CurrentVersionString,
                Sync.handlers.Where(h => h.debugOnly).Select(h => h.syncId).ToHashSet(),
                Sync.handlers.Where(h => h.hostOnly).Select(h => h.syncId).ToHashSet(),
                (MultiplayerData.modCtorRoundMode, MultiplayerData.staticCtorRoundMode),
                new Dictionary<string, DefInfo>(MultiplayerData.localDefInfos)
            ));
        }

        [PacketHandler(Packets.Server_UsernameOk)]
        public void HandleUsernameOk(ByteReader data) =>
            connection.SendFragmented(new ClientJoinDataPacket
            {
                modCtorRoundMode = MultiplayerData.modCtorRoundMode,
                staticCtorRoundMode = MultiplayerData.staticCtorRoundMode,
                defInfos = MultiplayerData.localDefInfos.Select(kv => new KeyedDefInfo
                    { name = kv.Key, count = kv.Value.count, hash = kv.Value.hash }).ToArray()
            }.Serialize());

        [PacketHandler(Packets.Server_JoinData, allowFragmented: true)]
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
            var defStatusMap = new Dictionary<DefInfo, DefCheckStatus>();
            foreach (var local in MultiplayerData.localDefInfos)
            {
                var status = defsData.ReadEnum<DefCheckStatus>();
                defStatusMap.Add(local.Value, status);

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
                    .Select(kv => (name: kv.Key, def: kv.Value, status: defStatusMap[kv.Value]))
                    .Where(kv => kv.status != DefCheckStatus.Ok)
                    .Take(10)
                    .Join(kv => $"{kv.name}: {kv.status}", "\n");

                Find.WindowStack.Add(new JoinDataWindow(remoteInfo){
                    connectAnywayDisabled = defDiff ? "MpMismatchDefsDiff".Translate() + defDiffStr : null,
                    connectAnywayCallback = StartDownloading
                });

                void StartDownloading()
                {
                    connection.Send(Packets.Client_WorldRequest);
                    connection.ChangeState(ConnectionStateEnum.ClientLoading);
                }
            }
        }
    }

}
