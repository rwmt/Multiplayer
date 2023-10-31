using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Multiplayer.Common.Util;

namespace Multiplayer.Common
{
    public class LiteNetManager
    {
        private MultiplayerServer server;

        public List<(LiteNetEndpoint, NetManager)> netManagers = new();
        public NetManager? lanManager;
        private NetManager? arbiter;

        public int ArbiterPort => arbiter!.LocalPort;

        private int broadcastTimer;

        public LiteNetManager(MultiplayerServer server)
        {
            this.server = server;
        }

        public void Tick()
        {
            foreach (var (_, man) in netManagers)
                man.PollEvents();

            lanManager?.PollEvents();
            arbiter?.PollEvents();

            if (lanManager != null && broadcastTimer % 60 == 0)
                lanManager.SendBroadcast("mp-server"u8.ToArray(), 5100);

            broadcastTimer++;
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
                        ServerLog.Detail($"Starting NetManager at {endpoint}");
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
        public IPAddress? ipv4;
        public IPAddress? ipv6;
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
