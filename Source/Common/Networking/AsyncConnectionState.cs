using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Multiplayer.Common.Networking.Packet;
using Multiplayer.Common.Util;

namespace Multiplayer.Common;

public abstract class AsyncConnectionState(ConnectionBase connection) : MpConnectionState(connection)
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private PacketAwaitable<ByteReader?>? packetAwaitable;
    private long waitDeadlineMs = long.MaxValue;

    public Task? CurrentTask { get; private set; }

    /// The packet this state is waiting for, if any.
    public Packets? AwaitedPacket => packetAwaitable?.PacketType;

    /// True when the awaited packet didn't arrive within the timeout given when starting to wait. Checked by the
    /// server loop, which disconnects the player.
    public bool WaitTimedOut => packetAwaitable != null && Clock.ElapsedMilliseconds > waitDeadlineMs;

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
    protected PacketAwaitable<ByteReader> Packet(Packets packet, TimeSpan? timeout = null)
    {
        if (packetAwaitable != null)
            throw new Exception($"Already waiting for another packet: {packetAwaitable}");

        ServerLog.Verbose($"{connection} waiting for {packet}");

        var awaitable = new PacketAwaitable<ByteReader?>(packet, false);
        StartWaiting(awaitable, timeout);
        return awaitable!;
    }

    /// <summary>
    /// Wait for a packet of the given type. The packet must arrive after the call to this method.
    /// An exception is thrown if this is called again before the packet arrives. The player is disconnected if a
    /// different packet type arrives.
    /// </summary>
    protected PacketAwaitable<T> TypedPacket<T>(TimeSpan? timeout = null) where T: struct, IPacket
    {
        var typed = new PacketAwaitable<T>(PacketTypeInfo<T>.Id, announcePacketFailure: false);
        WaitForTypedPacket<T>(announcePacketFailure: false, timeout, packet =>
        {
            if (packet.HasValue)
                typed.SetResult(packet.Value);
            else
                // Packet failure isn't announced for this awaitable, so this shouldn't happen
                typed.SetException(new InvalidOperationException($"Didn't receive {PacketTypeInfo<T>.Id}"));
        }, typed.SetException);
        return typed;
    }

    /// <summary>
    /// Wait for a packet of the given type. The packet must arrive after the call to this method.
    /// An exception is thrown if this is called again before the packet arrives. The player is disconnected if a
    /// different packet type arrives.
    /// The result is null if the player is disconnected while waiting.
    /// </summary>
    protected PacketAwaitable<ByteReader?> PacketOrNull(Packets packet, TimeSpan? timeout = null)
    {
        if (packetAwaitable != null)
            throw new Exception($"Already waiting for another packet: {packetAwaitable}");

        ServerLog.Verbose($"{connection} waiting for {packet}");

        var awaitable = new PacketAwaitable<ByteReader?>(packet, true);
        StartWaiting(awaitable, timeout);
        return awaitable;
    }


    /// <summary>
    /// Wait for a packet of the given type. The packet must arrive after the call to this method.
    /// An exception is thrown if this is called again before the packet arrives. The player is disconnected if a
    /// different packet type arrives.
    /// </summary>
    protected PacketAwaitable<T?> TypedPacketOrNull<T>(TimeSpan? timeout = null) where T: struct, IPacket
    {
        var typed = new PacketAwaitable<T?>(PacketTypeInfo<T>.Id, announcePacketFailure: true);
        WaitForTypedPacket<T>(announcePacketFailure: true, timeout, typed.SetResult, typed.SetException);
        return typed;
    }

    private void WaitForTypedPacket<T>(bool announcePacketFailure, TimeSpan? timeout, Action<T?> onResult,
        Action<Exception> onError) where T : struct, IPacket
    {
        if (packetAwaitable != null)
            throw new Exception($"Already waiting for another packet: {packetAwaitable}");

        ServerLog.Verbose($"{connection} waiting for {PacketTypeInfo<T>.Id}");

        var raw = new PacketAwaitable<ByteReader?>(PacketTypeInfo<T>.Id, announcePacketFailure);
        if (PacketTypeInfo<T>.AllowFragmented) raw.Fragmented();

        // Bind and resume the awaiting state inline, on the thread that handles the packet (the server thread).
        // Task continuations used to move the rest of the state onto the thread pool, racing the server loop.
        raw.OnCompleted(() =>
        {
            var reader = raw.GetResult();
            if (reader == null)
            {
                onResult(null);
                return;
            }

            var packet = default(T);
            try
            {
                packet.Bind(new PacketReader(reader));
            }
            catch (Exception e)
            {
                ServerLog.Error($"Failed to bind packet {PacketTypeInfo<T>.Id}: {e}");
                onError(e);
                return;
            }

            onResult(packet);
        });

        StartWaiting(raw, timeout);
    }

    private void StartWaiting(PacketAwaitable<ByteReader?> awaitable, TimeSpan? timeout)
    {
        packetAwaitable = awaitable;
        waitDeadlineMs = timeout is { } t ? Clock.ElapsedMilliseconds + (long)t.TotalMilliseconds : long.MaxValue;
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
}

public class PacketAwaitable<T>(Packets packetType, bool announcePacketFailure) : INotifyCompletion
{
    private List<Action> continuations = new();
    private bool completed;
    private T result = default!;
    private ExceptionDispatchInfo? exception;

    public Packets PacketType { get; } = packetType;
    public bool AnnouncePacketFailure { get; } = announcePacketFailure;
    public bool Fragment { get; private set; }

    public bool IsCompleted => completed;
    public PacketAwaitable<T> GetAwaiter() => this;

    public void OnCompleted(Action continuation)
    {
        if (completed) continuation();
        else continuations.Add(continuation);
    }

    public T GetResult()
    {
        exception?.Throw();
        return result;
    }

    public void SetResult(T r)
    {
        result = r;
        Complete();
    }

    public void SetException(Exception e)
    {
        exception = ExceptionDispatchInfo.Capture(e);
        Complete();
    }

    // Continuations run synchronously on the completing thread
    private void Complete()
    {
        if (completed) throw new InvalidOperationException($"Awaitable for {PacketType} completed twice");
        completed = true;

        var toRun = continuations;
        continuations = [];
        foreach (var continuation in toRun)
            continuation();
    }

    public override string ToString() => PacketType.ToString();

    public PacketAwaitable<T> Fragmented()
    {
        Fragment = true;
        return this;
    }
}
