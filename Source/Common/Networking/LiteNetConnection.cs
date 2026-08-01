using LiteNetLib;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common
{
    public class LiteNetConnection(NetPeer peer) : ConnectionBase
    {
        public readonly NetPeer peer = peer;

        // Only a hint at who the remote is: players behind one NAT share an address, so a housemate
        // can match; and a player whose address changed (mobile, VPN, or v4 vs v6 across the two
        // NetManagers) won't match their own earlier connection.
        public override object? RemoteIdentity => peer.Address;

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            if (peer.ConnectionState == ConnectionState.Connected)
                peer.Send(raw, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
            else
                ServerLog.Error($"SendRaw() called with invalid connection state ({peer}): {peer.ConnectionState}");
        }

        protected override void OnClose(ServerDisconnectPacket? goodbye)
        {
            if (goodbye.HasValue)
                peer.Disconnect(goodbye.Value.Serialize().data);
            else
                peer.Disconnect();
        }

        public override void OnKeepAliveArrived(bool idMatched)
        {
            // Latency already handled by LiteNetLib. This can be as low as 0ms because LNL spawns its own thread for
            // receiving packets and immediately processes its own internal keep alive packet (called Ping-Pong).
            // This is handled only on the server-side in MpServerNetListener
        }

        public override string ToString()
        {
            return $"NetConnection ({peer}) ({username})";
        }
    }
}
