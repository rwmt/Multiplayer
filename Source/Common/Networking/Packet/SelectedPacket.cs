using System;
using System.Collections.Generic;

namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Selected)]
public record struct ServerSelectedPacket(int playerId, ClientSelectedPacket data) : IPacket
{
    public int playerId = playerId;
    public ClientSelectedPacket data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref data);
    }
}

/// A change of the player's selection: reset clears it first, then the ids are added and removed
[PacketDefinition(Packets.Client_Selected)]
public record struct ClientSelectedPacket : IPacket
{
    // Per packet and list. Vanilla selects at most 200 things, but mods can select more.
    public const int MaxIds = 200;

    public bool reset;
    public int[] newlySelectedIds;
    public int[] unselectedIds;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref reset);
        buf.Bind(ref newlySelectedIds, BinderOf.Int(), maxLength: MaxIds);
        buf.Bind(ref unselectedIds, BinderOf.Int(), maxLength: MaxIds);
    }

    /// Splits a selection change into packets within the limits. Only the first one resets, so applying them in
    /// order (they're sent reliably) gives the whole change.
    public static IEnumerable<ClientSelectedPacket> Split(bool reset, int[] added, int[] removed)
    {
        var count = Math.Max(1, (Math.Max(added.Length, removed.Length) + MaxIds - 1) / MaxIds);
        for (int i = 0; i < count; i++)
            yield return new ClientSelectedPacket
            {
                reset = reset && i == 0,
                newlySelectedIds = Slice(added, i * MaxIds),
                unselectedIds = Slice(removed, i * MaxIds)
            };
    }

    private static int[] Slice(int[] ids, int start)
    {
        var length = Math.Max(0, Math.Min(MaxIds, ids.Length - start));
        var slice = new int[length];
        Array.Copy(ids, Math.Min(start, ids.Length), slice, 0, length);
        return slice;
    }
}
