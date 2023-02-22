using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Multiplayer.Common.Util;

namespace Multiplayer.Common;

public abstract class AsyncConnectionState : MpConnectionState
{
    private PacketAwaitable<ByteReader?>? packetAwaitable;

    public Task? CurrentTask { get; private set; }

    public AsyncConnectionState(ConnectionBase connection) : base(connection)
    {
    }

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

    public override PacketHandlerInfo? GetPacketHandler(Packets packet)
    {
        return packetAwaitable != null && packetAwaitable.PacketType == packet ?
            new PacketHandlerInfo((_, args) =>
            {
                var source = packetAwaitable;
                packetAwaitable = null;
                source.SetResult((ByteReader)args[0]);
                return null;
            }, packetAwaitable.Fragment) :
            null;
    }

    protected async Task<bool> EndIfDead()
    {
        if (!alive)
            await new Blackhole();
        return true;
    }
}

public class PacketAwaitable<T> : INotifyCompletion
{
    private List<Action> continuations = new();
    public Packets PacketType { get; }
    public bool AnnouncePacketFailure { get; }
    private T? result;

    public bool Fragment { get; private set; }

    public PacketAwaitable(Packets packetType, bool announcePacketFailure)
    {
        PacketType = packetType;
        AnnouncePacketFailure = announcePacketFailure;
    }

    public void OnCompleted(Action continuation)
    {
        continuations.Add(continuation);
    }

    public bool IsCompleted => result != null;

    public T GetResult()
    {
        return result!;
    }

    public PacketAwaitable<T> GetAwaiter() => this;

    public void SetResult(T r)
    {
        result = r;
        foreach (var continuation in continuations)
            continuation();
    }

    public override string ToString()
    {
        return PacketType.ToString();
    }

    public PacketAwaitable<T> Fragmented()
    {
        Fragment = true;
        return this;
    }
}
