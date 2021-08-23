using Multiplayer.Common;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client.Networking
{
    public abstract class SteamBaseConn : IConnection
    {
        public readonly CSteamID remoteId;

        public readonly ushort recvChannel; // currently only for client
        public readonly ushort sendChannel; // currently only for server

        public SteamBaseConn(CSteamID remoteId, ushort recvChannel, ushort sendChannel)
        {
            this.remoteId = remoteId;
            this.recvChannel = recvChannel;
            this.sendChannel = sendChannel;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            byte[] full = new byte[1 + raw.Length];
            full[0] = reliable ? (byte)2 : (byte)0;
            raw.CopyTo(full, 1);

            SendRawSteam(full, reliable);
        }

        public void SendRawSteam(byte[] raw, bool reliable)
        {
            SteamNetworking.SendP2PPacket(
                remoteId,
                raw,
                (uint)raw.Length,
                reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliable,
                sendChannel
            );
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
            if (State != ConnectionStateEnum.ClientSteam)
                Send(Packets.Special_Steam_Disconnect, GetDisconnectBytes(reason, data));
        }

        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
            {
                OnDisconnect();
            }
            else
            {
                base.HandleReceive(msgId, fragState, reader, reliable);
            }
        }

        public abstract void OnError(EP2PSessionError error);

        protected abstract void OnDisconnect();

        public override string ToString()
        {
            return $"SteamP2P ({remoteId}) ({username})";
        }
    }

    public class SteamClientConn : SteamBaseConn
    {
        static ushort RandomChannelId() => (ushort)new Random().Next();

        public SteamClientConn(CSteamID remoteId) : base(remoteId, RandomChannelId(), 0)
        {
        }

        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
                Multiplayer.session.ProcessDisconnectPacket((MpDisconnectReason)reader.ReadByte(), reader.ReadPrefixedBytes());

            base.HandleReceive(msgId, fragState, reader, reliable);
        }

        public override void OnError(EP2PSessionError error)
        {
            Multiplayer.session.disconnectReasonTranslated =
                error == EP2PSessionError.k_EP2PSessionErrorTimeout ? "MpSteamTimedOut".Translate() : "MpSteamGenericError".Translate();

            OnDisconnect();
        }

        protected override void OnDisconnect()
        {
            ConnectionStatusListeners.TryNotifyAll_Disconnected();
            Multiplayer.StopMultiplayer();
        }
    }

    public class SteamServerConn : SteamBaseConn
    {
        public SteamServerConn(CSteamID remoteId, ushort clientChannel) : base(remoteId, 0, clientChannel)
        {
        }

        public override void OnError(EP2PSessionError error)
        {
            OnDisconnect();
        }

        protected override void OnDisconnect()
        {
            serverPlayer.Server.OnDisconnected(this, MpDisconnectReason.ClientLeft);
        }
    }
}
