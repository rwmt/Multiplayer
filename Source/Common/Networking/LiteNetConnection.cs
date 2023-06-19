using LiteNetLib;

namespace Multiplayer.Common
{
    public class LiteNetConnection : ConnectionBase
    {
        public readonly NetPeer peer;

        public LiteNetConnection(NetPeer peer)
        {
            this.peer = peer;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            peer.Send(raw, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

        public override void Close(MpDisconnectReason reason, byte[]? data)
        {
            peer.NetManager.TriggerUpdate(); // todo: is this needed?
            peer.NetManager.DisconnectPeer(peer, GetDisconnectBytes(reason, data));
        }

        public override string ToString()
        {
            return $"NetConnection ({peer.EndPoint}) ({username})";
        }
    }
}
