using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Multiplayer.Common.Networking.Packet;
using Multiplayer.Common.Util;

namespace Multiplayer.Common;

public abstract class AsyncConnectionState(ConnectionBase connection) : MpConnectionState(connection)
{
    private PacketAwaitable<ByteReader?>? packetAwaitable;

    public Task? CurrentTask { get; private set; }

    public override void StartState()
    {
        async Task RunStateCatch()
        {
            try
            {
                await RunState();
            }
            catch (Exception e)
            {
                ServerLog.Error($"Exception in state {GetType().Name}: {e}");
                Player.Disconnect(MpDisconnectReason.StateException);
            }
        }

        CurrentTask = RunStateCatch();
    }

    protected abstract Task RunState();

    public override void OnDisconnect()
    {
        if (packetAwaitable is { AnnouncePacketFailure: true })
        {
            var source = packetAwaitable;
            packetAwaitable = null;
            source.SetResult(null);
        }
    }

    /// <summary>
    /// Wait for a packet of the given type. The packet must arrive after the call to this method.
    /// An exception is thrown if this is called again before the packet arrives. The player is disconnected if a
    /// different packet type arrives.
    /// </summary>
    protected PacketAwaitable<ByteReader> Packet(Packets packet)
    {
        if (packetAwaitable != null)
            throw new Exception($"Already waiting for another packet: {packetAwaitable}");

        ServerLog.Verbose($"{connection} waiting for {packet}");

        packetAwaitable = new PacketAwaitable<ByteReader?>(packet, false);
        return packetAwaitable!;
    }

    /// <summary>
    /// Wait for a packet of the given type. The packet must arrive after the call to this method.
    /// An exception is thrown if this is called again before the packet arrives. The player is disconnected if a
    /// different packet type arrives.
    /// </summary>
    protected Task<T> TypedPacket<T>() where T: struct, IPacket
    {
        if (packetAwaitable != null)
            throw new Exception($"Already waiting for another packet: {packetAwaitable}");

        ServerLog.Verbose($"{connection} waiting for {PacketTypeInfo<T>.Id}");

        packetAwaitable = TypedPacketAwaitable<T>(out var task, announcePacketFailure: false);
        return task
            .ContinueWith(finishedTask => (T)finishedTask.Result!, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    /// <summary>
    /// Wait for a packet of the given type. The packet must arrive after the call to this method.
    /// An exception is thrown if this is called again before the packet arrives. The player is disconnected if a
    /// different packet type arrives.
    /// The result is null if the player is disconnected while waiting.
    /// </summary>
    protected PacketAwaitable<ByteReader?> PacketOrNull(Packets packet)
    {
        if (packetAwaitable != null)
            throw new Exception($"Already waiting for another packet: {packetAwaitable}");

        ServerLog.Verbose($"{connection} waiting for {packet}");

        packetAwaitable = new PacketAwaitable<ByteReader?>(packet, true);
        return packetAwaitable;
    }


    /// <summary>
    /// Wait for a packet of the given type. The packet must arrive after the call to this method.
    /// An exception is thrown if this is called again before the packet arrives. The player is disconnected if a
    /// different packet type arrives.
    /// </summary>
    protected Task<T?> TypedPacketOrNull<T>() where T: struct, IPacket
    {
        if (packetAwaitable != null)
            throw new Exception($"Already waiting for another packet: {packetAwaitable}");

        ServerLog.Verbose($"{connection} waiting for {PacketTypeInfo<T>.Id}");

        packetAwaitable = TypedPacketAwaitable<T>(out var task, announcePacketFailure: true);
        return task;
    }

    public override PacketHandlerInfo? GetPacketHandler(Packets packet)
    {
        if (packetAwaitable != null && packetAwaitable.PacketType == packet)
            return new PacketHandlerInfo((_, data) =>
            {
                var source = packetAwaitable;
                packetAwaitable = null;
                source.SetResult(data);
            }, packetAwaitable.Fragment);

        return base.GetPacketHandler(packet);
    }

    protected async Task<bool> EndIfDead()
    {
        if (!alive)
            await new Blackhole();
        return true;
    }

    private static PacketAwaitable<ByteReader?> TypedPacketAwaitable<TPacket>(out Task<TPacket?> task, bool announcePacketFailure) where TPacket : struct, IPacket
    {
        var awaitable = new PacketAwaitable<ByteReader?>(PacketTypeInfo<TPacket>.Id, announcePacketFailure);
        if (PacketTypeInfo<TPacket>.AllowFragmented) awaitable.Fragmented();

        task = CreateTask();
        return awaitable;

        async Task<TPacket?> CreateTask()
        {
            // Announce packet failure is false, so this won't be null.
            var reader = await awaitable;
            if (reader == null) return null;
            var packet = default(TPacket);
            try
            {
                packet.Bind(new PacketReader(reader));
            }
            catch (Exception e)
            {
                ServerLog.Error($"Failed to bind packet {PacketTypeInfo<TPacket>.Id}: {e}");
                throw;
            }
            return packet;
        }
    }
}

public class PacketAwaitable<T>(Packets packetType, bool announcePacketFailure) : INotifyCompletion
{
    private List<Action> continuations = new();
    public Packets PacketType { get; } = packetType;
    public bool AnnouncePacketFailure { get; } = announcePacketFailure;
    public bool Fragment { get; private set; }
    private T? result;

    public void OnCompleted(Action continuation) => continuations.Add(continuation);
    public bool IsCompleted => result != null;
    public T GetResult() => result!;
    public PacketAwaitable<T> GetAwaiter() => this;

    public void SetResult(T r)
    {
        result = r;
        foreach (var continuation in continuations)
            continuation();
    }

    public override string ToString() => PacketType.ToString();

    public PacketAwaitable<T> Fragmented()
    {
        Fragment = true;
        return this;
    }
}
