using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LiteNetLib;
using Multiplayer.Common.Util;

namespace Multiplayer.Common
{
    public class LiteNetManager
    {
        private MultiplayerServer server;

        public List<(LiteNetEndpoint, NetManager)> netManagers = new();
        public NetManager lanManager;
        private NetManager arbiter;

        public int ArbiterPort => arbiter.LocalPort;

        public int NetTimer { get; private set; }

        public LiteNetManager(MultiplayerServer server)
        {
            this.server = server;
        }

        public void TickNet()
        {
            foreach (var (_, man) in netManagers)
                man.PollEvents();

            lanManager?.PollEvents();
            arbiter?.PollEvents();

            if (lanManager != null && NetTimer % 60 == 0)
                lanManager.SendBroadcast(Encoding.UTF8.GetBytes("mp-server"), 5100);

            NetTimer++;

            if (NetTimer % 60 == 0)
                server.playerManager.SendLatencies();

            if (NetTimer % 30 == 0)
                foreach (var player in server.PlayingPlayers)
                    player.SendPacket(Packets.Server_KeepAlive, ByteWriter.GetBytes(player.keepAliveId), false);

            if (NetTimer % 2 == 0)
                server.SendToAll(Packets.Server_TimeControl, ByteWriter.GetBytes(server.gameTimer, server.commands.NextCmdId), false);
        }

        public void StartNet()
        {
            try
            {
                if (server.settings.direct)
                {
                    var liteNetEndpoints = new Dictionary<int, LiteNetEndpoint>();
                    var split = server.settings.directAddress.Split(MultiplayerServer.EndpointSeparator);

                    foreach (var str in split)
                        if (Endpoints.TryParse(str, MultiplayerServer.DefaultPort, out var endpoint))
                        {
                            if (endpoint.AddressFamily == AddressFamily.InterNetwork)
                                liteNetEndpoints.GetOrAddNew(endpoint.Port).ipv4 = endpoint.Address;
                            else if (endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                                liteNetEndpoints.GetOrAddNew(endpoint.Port).ipv6 = endpoint.Address;
                        }

                    foreach (var (port, endpoint) in liteNetEndpoints)
                    {
                        endpoint.port = port;
                        netManagers.Add((endpoint, CreateNetManager(endpoint.ipv6 != null ? IPv6Mode.SeparateSocket : IPv6Mode.Disabled)));
                    }

                    foreach (var (endpoint, man) in netManagers)
                    {
                        man.Start(endpoint.ipv4 ?? IPAddress.Any, endpoint.ipv6 ?? IPAddress.IPv6Any, endpoint.port);
                    }
                }
            }
            catch (Exception e)
            {
                ServerLog.Log($"Exception starting direct: {e}");
            }

            try
            {
                if (server.settings.lan)
                {
                    lanManager = CreateNetManager(IPv6Mode.Disabled);
                    lanManager.Start(IPAddress.Parse(server.settings.lanAddress), IPAddress.IPv6Any, 0);
                }
            }
            catch (Exception e)
            {
                ServerLog.Log($"Exception starting LAN: {e}");
            }

            NetManager CreateNetManager(IPv6Mode ipv6)
            {
                return new NetManager(new MpServerNetListener(server, false))
                {
                    EnableStatistics = true,
                    IPv6Enabled = ipv6
                };
            }
        }

        public void StopNet()
        {
            foreach (var (_, man) in netManagers)
                man.Stop();
            netManagers.Clear();
            lanManager?.Stop();
        }

        public void SetupArbiterConnection()
        {
            arbiter = new NetManager(new MpServerNetListener(server, true)) { IPv6Enabled = IPv6Mode.Disabled };
            arbiter.Start(IPAddress.Loopback, IPAddress.IPv6Any, 0);
        }

        public void OnServerStop()
        {
            StopNet();
            arbiter?.Stop();
        }
    }

    public class LiteNetEndpoint
    {
        public IPAddress ipv4;
        public IPAddress ipv6;
        public int port;

        public override string ToString()
        {
            return
                ipv4 == null ? $"{ipv6}:{port}" :
                ipv6 == null ? $"{ipv4}:{port}" :
                $"{ipv4}:{port} / {ipv6}:{port}";
        }
    }
}
