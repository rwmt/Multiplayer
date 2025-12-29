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

    // We want to inherit the shared typed packet handlers from ClientBaseState (keepalive, time control, disconnect).
    // Disabling inheritance can cause missing core handlers during joining and lead to early disconnects / broken UI.
    [PacketHandlerClass(inheritHandlers: true)]
    public class ClientJoiningState : ClientBaseState
    {
        public ClientJoiningState(ConnectionBase connection) : base(connection)
        {
        }

        [TypedPacketHandler]
        public void HandleBootstrap(ServerBootstrapPacket packet)
        {
            // Server informs us early that it's in bootstrap/configuration mode.
            // Full UI/flow is handled on the client side; for now we just persist the flag
            // so receiving the packet doesn't error during join (tests rely on this).
            Multiplayer.session.serverIsInBootstrap = packet.bootstrap;
            Multiplayer.session.serverBootstrapSettingsMissing = packet.settingsMissing;
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

        [TypedPacketHandler]
        public void HandleInitDataRequest(ServerInitDataRequestPacket packet) =>
            connection.SendFragmented(PackInitData(packet.includeConfigs).ToNet().Serialize());

        public static ServerInitData PackInitData(bool includeConfigs) => new(
            JoinData.WriteServerData(includeConfigs),
            VersionControl.CurrentVersionString,
            Sync.handlers.Where(h => h.debugOnly).Select(h => h.syncId).ToHashSet(),
            Sync.handlers.Where(h => h.hostOnly).Select(h => h.syncId).ToHashSet(),
            (MultiplayerData.modCtorRoundMode, MultiplayerData.staticCtorRoundMode),
            new Dictionary<string, DefInfo>(MultiplayerData.localDefInfos)
        );

        [PacketHandler(Packets.Server_UsernameOk)]
        public void HandleUsernameOk(ByteReader data) =>
            connection.SendFragmented(new ClientJoinDataPacket
            {
                modCtorRoundMode = MultiplayerData.modCtorRoundMode,
                staticCtorRoundMode = MultiplayerData.staticCtorRoundMode,
                defInfos = MultiplayerData.localDefInfos.Select(kv => new KeyedDefInfo
                    { name = kv.Key, count = kv.Value.count, hash = kv.Value.hash }).ToArray()
            }.Serialize());

        [TypedPacketHandler]
        public void HandleJoinData(ServerJoinDataPacket packet)
        {
            Multiplayer.session.gameName = packet.gameName;
            Multiplayer.session.playerId = packet.playerId;

            var remoteInfo = new RemoteData
            {
                remoteRwVersion = packet.rwVersion,
                remoteMpVersion = packet.mpVersion,
                remoteAddress = Multiplayer.session.address,
                remotePort = Multiplayer.session.port,
                remoteSteamHost = Multiplayer.session.steamHost
            };

            var defDiff = false;
            var defStatusMap = new Dictionary<DefInfo, DefCheckStatus>();
            var i = 0;
            foreach (var local in MultiplayerData.localDefInfos)
            {
                var status = packet.defStatus[i++];
                defStatusMap.Add(local.Value, status);

                if (status != DefCheckStatus.Ok)
                    defDiff = true;
            }

            JoinData.ReadServerData(packet.rawServerInitData, remoteInfo);

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
                    if (Multiplayer.session.serverIsInBootstrap)
                    {
                        // Server is in bootstrap/configuration mode: don't request world data.
                        // Instead, show a dedicated configuration UI.
                        connection.ChangeState(ConnectionStateEnum.ClientBootstrap);
                        Find.WindowStack.Add(new BootstrapConfiguratorWindow(connection));
                        return;
                    }

                    connection.Send(Packets.Client_WorldRequest);
                    connection.ChangeState(ConnectionStateEnum.ClientLoading);
                }
            }
        }
    }

}
