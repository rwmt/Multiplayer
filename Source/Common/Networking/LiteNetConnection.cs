using LiteNetLib;

namespace Multiplayer.Common
{
    public class LiteNetConnection(NetPeer peer) : ConnectionBase
    {
        public readonly NetPeer peer = peer;

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            if (peer.ConnectionState == ConnectionState.Connected)
                peer.Send(raw, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
            else
                ServerLog.Error($"SendRaw() called with invalid connection state ({peer.EndPoint}): {peer.ConnectionState}");
        }

        public override void Close(MpDisconnectReason reason, byte[]? data)
        {
            peer.NetManager.TriggerUpdate(); // todo: is this needed?
            peer.NetManager.DisconnectPeer(peer, GetDisconnectBytes(reason, data));
        }

        public override void OnKeepAliveArrived(bool idMatched)
        {
            // Latency already handled by LiteNetLib. This can be as low as 0ms because LNL spawns its own thread for
            // receiving packets and immediately processes its own internal keep alive packet (called Ping-Pong).
            // This is handled only on the server-side in MpServerNetListener
        }

        public override string ToString()
        {
            return $"NetConnection ({peer.EndPoint}) ({username})";
        }
    }
}
