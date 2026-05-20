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
    [PacketHandlerClass(inheritHandlers: false)]
    public class ClientJoiningState(ConnectionBase connection, string username) : ClientBaseState(connection)
    {
        private BootstrapServerState? bootstrapState;

        [TypedPacketHandler]
        public new void HandleDisconnected(ServerDisconnectPacket packet) => base.HandleDisconnected(packet);

        [TypedPacketHandler]
        public void HandleBootstrap(ServerBootstrapPacket packet) =>
            bootstrapState = BootstrapServerState.FromPacket(packet);

        public override void StartState()
        {
            connection.Send(ClientProtocolPacket.Current());
            ConnectionStatusListeners.TryNotifyAll_Connected();
        }

        [TypedPacketHandler]
        public void HandleProtocolOk(ServerProtocolOkPacket packet)
        {
            Multiplayer.session.isStandaloneServer = packet.isStandaloneServer;
            Multiplayer.session.autosaveInterval = packet.autosaveInterval;
            Multiplayer.session.autosaveUnit = packet.autosaveUnit;

            if (packet.hasPassword)
            {
                // Delay showing the window for better UX
                OnMainThread.Schedule(() => Find.WindowStack.Add(new GamePasswordWindow(username)
                {
                    returnToServerBrowser = Find.WindowStack.WindowOfType<BaseConnectingWindow>().returnToServerBrowser
                }), 0.3f);
            }
            else
            {
                connection.Send(new ClientUsernamePacket(username));
            }
        }

        [TypedPacketHandler]
        public void HandleInitDataRequest(ServerInitDataRequestPacket packet) =>
            connection.SendFragmented(CreateInitDataPacket(packet.includeConfigs).Serialize());

        public static ClientInitDataPacket CreateInitDataPacket(bool includeConfigs) => new()
        {
            rwVersion = VersionControl.CurrentVersionString,
            debugOnlySyncCmds = Sync.handlers.Where(h => h.debugOnly).Select(h => h.syncId).ToHashSet().ToArray(),
            hostOnlySyncCmds = Sync.handlers.Where(h => h.hostOnly).Select(h => h.syncId).ToHashSet().ToArray(),
            modCtorRoundMode = MultiplayerData.modCtorRoundMode,
            staticCtorRoundMode = MultiplayerData.staticCtorRoundMode,
            defInfos = MultiplayerData.localDefInfos
                .Select(kv => new KeyedDefInfo { name = kv.Key, count = kv.Value.count, hash = kv.Value.hash })
                .ToArray(),
            includeConfigs = includeConfigs,
            Mods = JoinData.WriteServerData(includeConfigs)
        };

        [PacketHandler(Packets.Server_UsernameOk)]
        public void HandleUsernameOk(ByteReader data) =>
            connection.SendFragmented(new ClientJoinDataPacket
            {
                modCtorRoundMode = MultiplayerData.modCtorRoundMode,
                staticCtorRoundMode = MultiplayerData.staticCtorRoundMode,
                defInfos = MultiplayerData.localDefInfos
                    .Select(kv => new KeyedDefInfo { name = kv.Key, count = kv.Value.count, hash = kv.Value.hash })
                    .ToArray()
            }.Serialize());

        [TypedPacketHandler]
        public void HandleJoinData(ServerJoinDataPacket packet)
        {
            Multiplayer.session.gameName = packet.gameName;
            Multiplayer.session.playerId = packet.playerId;

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

            var remoteInfo = RemoteData.FromNet(packet);

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

                Find.WindowStack.Add(new JoinDataWindow(remoteInfo, Multiplayer.session.connector)
                {
                    connectAnywayDisabled = defDiff ? "MpMismatchDefsDiff".Translate() + defDiffStr : null,
                    connectAnywayCallback = StartDownloading
                });

                void StartDownloading()
                {
                    if (bootstrapState is { Enabled: true } state)
                    {
                        var connectingWindows = Find.WindowStack.Windows
                            .OfType<BaseConnectingWindow>()
                            .ToList();

                        foreach (var connectingWindow in connectingWindows)
                        {
                            connectingWindow.suppressPostCloseActions = true;
                            Find.WindowStack.TryRemove(connectingWindow);
                        }

                        connection.ChangeState(ConnectionStateEnum.ClientBootstrap);
                        Find.WindowStack.Add(new BootstrapConfiguratorWindow(connection, state));
                        return;
                    }

                    connection.Send(Packets.Client_WorldRequest);
                    connection.ChangeState(ConnectionStateEnum.ClientLoading);
                }
            }
        }
    }

}
