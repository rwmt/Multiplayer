using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LiteNetLib;

namespace Multiplayer.Common
{
    public class LiteNetManager(MultiplayerServer server, IPEndPoint[] endpoints) : INetManager
    {
        public List<(LiteNetEndpoint endpoint, NetManager manager)> netManagers = [];

        public void Tick()
        {
            foreach (var (_, man) in netManagers) man.PollEvents();
        }

        public bool Start()
        {
            var success = true;

            var liteNetEndpoints = new Dictionary<int, LiteNetEndpoint>();
            foreach (var endpoint in endpoints)
            {
                if (endpoint.AddressFamily == AddressFamily.InterNetwork)
                    liteNetEndpoints.GetOrAddNew(endpoint.Port).ipv4 = endpoint.Address;
                else if (endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                    liteNetEndpoints.GetOrAddNew(endpoint.Port).ipv6 = endpoint.Address;
            }

            foreach (var (port, endpoint) in liteNetEndpoints)
            {
                endpoint.port = port;
                netManagers.Add((endpoint, CreateNetManager(endpoint.ipv6 != null)));
            }

            foreach (var (endpoint, man) in netManagers)
            {
                ServerLog.Detail($"Starting NetManager at {endpoint}");
                success &= man.Start(endpoint.ipv4 ?? IPAddress.Any, endpoint.ipv6 ?? IPAddress.IPv6Any, endpoint.port);
            }

            return success;

            NetManager CreateNetManager(bool ipv6)
            {
                return new NetManager(new MpServerNetListener(server, false))
                {
                    EnableStatistics = true,
                    IPv6Enabled = ipv6
                };
            }
        }

        public void Stop()
        {
            foreach (var (_, man) in netManagers) man.Stop();
            netManagers.Clear();
        }
    }

    public class LiteNetArbiterManager(MultiplayerServer server) : INetManager
    {
        private NetManager? arbiter;
        public int Port => arbiter!.LocalPort;

        public bool Start()
        {
            arbiter = new NetManager(new MpServerNetListener(server, true)) { IPv6Enabled = false };
            return arbiter.Start(IPAddress.Loopback, IPAddress.IPv6Any, 0);
        }

        public void Tick() => arbiter?.PollEvents();

        public void Stop() => arbiter?.Stop();
    }

    public class LiteNetLanManager(MultiplayerServer server, IPAddress lanAddress) : INetManager
    {
        private NetManager? lanManager;
        private int broadcastTimer;

        public bool Start()
        {
            lanManager = new NetManager(new MpServerNetListener(server, false))
            {
                EnableStatistics = true,
                IPv6Enabled = false
            };
            return lanManager.Start(lanAddress, IPAddress.IPv6Any, 0);
        }

        public void Tick()
        {
            if (lanManager == null) return;
            lanManager.PollEvents();
            if (broadcastTimer++ % 60 == 0)
                lanManager.SendBroadcast(Encoding.UTF8.GetBytes(MultiplayerServer.LanBroadcastName),
                    MultiplayerServer.LanBroadcastPort);
        }

        public void Stop() => lanManager?.Stop();
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
