using System;
using System.IO;

namespace Multiplayer.Common;

public record FragmentedPacket(
    Packets Id,
    MemoryStream Data,
    ushort ReceivedPartsCount,
    ushort ExpectedPartsCount,
    uint ReceivedSize,
    uint ExpectedSize)
{
    internal MemoryStream Data { get; } = Data;
    public uint ReceivedSize { get; internal set; } = ReceivedSize;
    public ushort ReceivedPartsCount { get; internal set; } = ReceivedPartsCount;

    internal static FragmentedPacket Create(Packets id, ushort expectedPartsCount, uint expectedSize) => new(id,
        new MemoryStream(Convert.ToInt32(expectedSize)), ReceivedPartsCount: 0, expectedPartsCount, 0, expectedSize);
}
