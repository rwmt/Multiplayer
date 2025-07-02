using System;
using System.Collections.Generic;

namespace Multiplayer.Common
{
    public abstract class ConnectionBase
    {
        public string? username;
        public ServerPlayer? serverPlayer;

        public virtual int Latency { get; set; }

        public ConnectionStateEnum State { get; private set; }
        public MpConnectionState? StateObj { get; private set; }
        // If lenient is set, reliable packets without handlers are ignored instead of throwing an exception.
        // This is set during rejoining and is usually caused by connection state mismatch.
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

        public void Send(Packets id)
        {
            Send(id, Array.Empty<byte>());
        }

        public void Send(Packets id, params object[] msg)
        {
            Send(id, ByteWriter.GetBytes(msg));
        }

        public virtual void Send(Packets id, byte[] message, bool reliable = true)
        {
            if (State == ConnectionStateEnum.Disconnected)
                return;

            if (message.Length > MaxSinglePacketSize)
                throw new PacketSendException($"Packet {id} too big for sending ({message.Length}>{MaxSinglePacketSize})");

            byte[] full = new byte[1 + message.Length];
            full[0] = (byte)(Convert.ToByte(id) & 0x3F);
            message.CopyTo(full, 1);

            SendRaw(full, reliable);
        }

        // Steam doesn't like messages bigger than a megabyte
        public const int MaxSinglePacketSize = 65_536;
        // We can send a single packet up to MaxSinglePacketSize but when specifically sending a fragmented packet,
        // use smaller sizes so that we have more control over them.
        public const int MaxFragmentPacketSize = 1024;
        public const int MaxPacketSize = 33_554_432;

        private const int FragNone = 0x0;
        private const int FragMore = 0x40;
        private const int FragEnd  = 0x80;

        private byte sendFragId;

        // All fragmented packets need to be sent from the same thread
        public void SendFragmented(Packets id, byte[] message)
        {
            if (State == ConnectionStateEnum.Disconnected)
                return;

            // +1 for Send metadata's overhead
            if (message.Length + 1 <= MaxFragmentPacketSize)
            {
                Send(id, message);
                return;
            }

            var fragId = sendFragId++;
            // every packet has an additional 2 bytes of overhead
            const int maxFragmentSize = MaxFragmentPacketSize - 2;
            // the first packet has an additional 6 bytes of overhead
            var totalLength = message.Length + 6;
            // Divide rounding up
            var fragParts = (totalLength + maxFragmentSize - 1) / maxFragmentSize;
            int read = 0;
            var writer = new ByteWriter(MaxFragmentPacketSize);
            while (read < message.Length)
            {
                // 1st packet contains 6 bytes of extra metadata.
                int len = read == 0
                    ? Math.Min(maxFragmentSize - 6, message.Length - read)
                    : Math.Min(maxFragmentSize, message.Length - read);
                int fragState = (read + len >= message.Length) ? FragEnd : FragMore;
                byte headerByte = (byte)((Convert.ToByte(id) & 0x3F) | fragState);

                // Write the packet id and fragment state: MORE or END
                writer.WriteByte(headerByte);
                writer.WriteByte(fragId);

                // Send the message length with the first packet
                if (read == 0)
                {
                    writer.WriteUShort(Convert.ToUInt16(fragParts));
                    writer.WriteUInt32(Convert.ToUInt32(message.Length));
                }

                // Copy the message fragment
                writer.WriteFrom(message, read, len);

                // SendRaw copies this data so we can freely clear the writer after it executes.
                SendRaw(writer.ToArray(), reliable: true);
                writer.SetLength(0);

                read += len;
            }
        }

        public void SendFragmented(Packets id, params object[] msg)
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

        private const int MaxFragmentedPackets = 1;
        private readonly List<(/* fragId */ byte, FragmentedPacket)> fragments = [];

        protected virtual void HandleReceiveMsg(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId is < 0 or >= (int)Packets.Count)
                throw new PacketReadException($"Bad packet id {msgId}");

            Packets packetType = (Packets)msgId;
            if (reader.Left > MaxSinglePacketSize)
                throw new PacketReadException($"Packet {packetType} too big {reader.Left}>{MaxSinglePacketSize}");

            var handler = StateObj?.GetPacketHandler(packetType);
            if (handler == null)
            {
                if (reliable && !Lenient)
                    throw new PacketReadException($"No handler for packet {packetType} in state {State}");
                ServerLog.Error($"No handler for packet {packetType} in state {State}");
                return;
            }

            if (fragState == FragNone) handler.Method(StateObj, reader);
            else HandleReceiveFragment(reader, packetType, handler);
        }

        private void HandleReceiveFragment(ByteReader reader, Packets packetType, PacketHandlerInfo handler)
        {
            if (!handler.Fragment) throw new PacketReadException($"Packet {packetType} can't be fragmented");
            if (reader.Left > MaxFragmentPacketSize)
                throw new PacketReadException($"Packet fragment {packetType} too big {reader.Left}>{MaxFragmentPacketSize}");

            var fragId = reader.ReadByte();
            var fragIndex = fragments.FindIndex(frag => frag.Item1 == fragId);
            FragmentedPacket fragPacket;
            if (fragIndex == -1)
            {
                if (fragments.Count >= MaxFragmentedPackets)
                    throw new PacketReadException(
                        $"High number of fragmented packets at once! {fragments.Count}/{MaxFragmentedPackets}. This will likely cause issues. Dropping the just received fragmented packet (packet type: {packetType}, fragment id: {fragId}).");

                var expectedParts = reader.ReadUShort();
                var expectedSize = reader.ReadUInt32();
                if (expectedParts < 2)
                    ServerLog.Error($"Received fragmented packet with only {expectedParts} expected parts (packet type: {packetType}, fragment id: {fragId}, expected size: {expectedSize}).");
                if (expectedSize > MaxPacketSize)
                    throw new PacketReadException($"Full packet {packetType} too big {expectedSize}>{MaxPacketSize}");

                fragPacket = FragmentedPacket.Create(packetType, expectedParts, expectedSize);
                fragIndex = fragments.Count;
                fragments.Add((fragId, fragPacket));
            }
            else
                (_, fragPacket) = fragments[fragIndex];

            if (fragPacket.Id != packetType)
                throw new PacketReadException(
                    $"Received fragment part with different packet id! {fragPacket.Id} != {packetType}");

            fragPacket.Data.Write(reader.GetBuffer(), reader.Position, reader.Left);
            fragPacket.ReceivedSize += Convert.ToUInt32(reader.Left);
            fragPacket.ReceivedPartsCount++;

            if (fragPacket.ReceivedPartsCount < fragPacket.ExpectedPartsCount)
            {
                handler.FragmentHandler?.Invoke(StateObj, fragPacket);
                return;
            }

            if (fragPacket.ReceivedSize != fragPacket.ExpectedSize)
                throw new PacketReadException($"Fragmented packet {packetType} (fragId {fragId}) recombined with different than expected size: {fragPacket.ReceivedSize} != {fragPacket.ExpectedSize}");

            fragments.RemoveAt(fragIndex);
            handler.Method(StateObj, new ByteReader(fragPacket.Data.GetBuffer()));
        }

        public abstract void Close(MpDisconnectReason reason, byte[]? data = null);

        public static byte[] GetDisconnectBytes(MpDisconnectReason reason, byte[]? data = null)
        {
            var writer = new ByteWriter();
            writer.WriteEnum(reason);
            writer.WritePrefixedBytes(data ?? Array.Empty<byte>());
            return writer.ToArray();
        }
    }
}
