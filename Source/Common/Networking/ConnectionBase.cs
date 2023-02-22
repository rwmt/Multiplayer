using System;

namespace Multiplayer.Common
{
    public abstract class ConnectionBase
    {
        public string? username;
        public ServerPlayer? serverPlayer;

        public virtual int Latency { get; set; }

        public ConnectionStateEnum State { get; private set; }
        public MpConnectionState? StateObj { get; private set; }
        public bool Lenient { get; set; }

        public T? GetState<T>() where T : MpConnectionState => (T?)StateObj;

        public void ChangeState(ConnectionStateEnum state)
        {
            if (StateObj != null)
                StateObj.alive = false;

            State = state;

            if (State == ConnectionStateEnum.Disconnected)
                StateObj = null;
            else
                StateObj = (MpConnectionState)Activator.CreateInstance(MpConnectionState.stateImpls[(int)state], this);

            StateObj?.StartState();
        }

        public virtual void Send(Packets id)
        {
            Send(id, Array.Empty<byte>());
        }

        public virtual void Send(Packets id, params object[] msg)
        {
            Send(id, ByteWriter.GetBytes(msg));
        }

        public virtual void Send(Packets id, byte[] message, bool reliable = true)
        {
            if (State == ConnectionStateEnum.Disconnected)
                return;

            if (message.Length > FragmentSize)
                throw new PacketSendException($"Packet {id} too big for sending ({message.Length}>{FragmentSize})");

            byte[] full = new byte[1 + message.Length];
            full[0] = (byte)(Convert.ToByte(id) & 0x3F);
            message.CopyTo(full, 1);

            SendRaw(full, reliable);
        }

        // Steam doesn't like messages bigger than a megabyte
        public const int FragmentSize = 65_536;
        public const int MaxPacketSize = 33_554_432;

        private const int FragNone = 0x0;
        private const int FragMore = 0x40;
        private const int FragEnd = 0x80;

        // All fragmented packets need to be sent from the same thread
        public virtual void SendFragmented(Packets id, byte[] message)
        {
            if (State == ConnectionStateEnum.Disconnected)
                return;

            int read = 0;
            while (read < message.Length)
            {
                int len = Math.Min(FragmentSize, message.Length - read);
                int fragState = (read + len >= message.Length) ? FragEnd : FragMore;
                byte headerByte = (byte)((Convert.ToByte(id) & 0x3F) | fragState);

                var writer = new ByteWriter(1 + 4 + len);

                // Write the packet id and fragment state: MORE or END
                writer.WriteByte(headerByte);

                // Send the message length with the first packet
                if (read == 0) writer.WriteInt32(message.Length);

                // Copy the message fragment
                writer.WriteFrom(message, read, len);

                SendRaw(writer.ToArray());

                read += len;
            }
        }

        public virtual void SendFragmented(Packets id, params object[] msg)
        {
            SendFragmented(id, ByteWriter.GetBytes(msg));
        }

        protected abstract void SendRaw(byte[] raw, bool reliable = true);

        public virtual void HandleReceiveRaw(ByteReader data, bool reliable)
        {
            if (State == ConnectionStateEnum.Disconnected)
                return;

            if (data.Left == 0)
                throw new PacketReadException("No packet id");

            byte info = data.ReadByte();
            byte msgId = (byte)(info & 0x3F);
            byte fragState = (byte)(info & 0xC0);

            HandleReceiveMsg(msgId, fragState, data, reliable);
        }

        private ByteWriter? fragmented;
        private int fullSize; // For information, doesn't affect anything

        public int FragmentProgress => (fragmented?.Position * 100 / fullSize) ?? 0;

        protected virtual void HandleReceiveMsg(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId is < 0 or >= (int)Packets.Count)
                throw new PacketReadException($"Bad packet id {msgId}");

            Packets packetType = (Packets)msgId;
            ServerLog.Verbose($"Received packet {this}: {packetType}");

            var handler = StateObj?.GetPacketHandler(packetType) ?? MpConnectionState.packetHandlers[(int)State, (int)packetType];
            if (handler == null)
            {
                if (reliable && !Lenient)
                    throw new PacketReadException($"No handler for packet {packetType} in state {State}");

                return;
            }

            if (fragState != FragNone && fragmented == null)
                fullSize = reader.ReadInt32();

            if (reader.Left > FragmentSize)
                throw new PacketReadException($"Packet {packetType} too big {reader.Left}>{FragmentSize}");

            if (fragState == FragNone)
            {
                handler.Method.Invoke(StateObj, reader);
            }
            else if (!handler.Fragment)
            {
                throw new PacketReadException($"Packet {packetType} can't be fragmented");
            }
            else
            {
                fragmented ??= new ByteWriter(reader.Left);
                fragmented.WriteRaw(reader.ReadRaw(reader.Left));

                if (fragmented.Position > MaxPacketSize)
                    throw new PacketReadException($"Full packet {packetType} too big {fragmented.Position}>{MaxPacketSize}");

                if (fragState == FragEnd)
                {
                    handler.Method.Invoke(StateObj, new ByteReader(fragmented.ToArray()));
                    fragmented = null;
                }
            }
        }

        public abstract void Close(MpDisconnectReason reason, byte[]? data = null);

        public static byte[] GetDisconnectBytes(MpDisconnectReason reason, byte[]? data = null)
        {
            var writer = new ByteWriter();
            writer.WriteByte((byte)reason);
            writer.WritePrefixedBytes(data ?? Array.Empty<byte>());
            return writer.ToArray();
        }
    }
}
