using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

public record ServerInitData(
    byte[] RawData,
    string RwVersion,
    HashSet<int> DebugOnlySyncCmds,
    HashSet<int> HostOnlySyncCmds,
    (RoundModeEnum, RoundModeEnum) RoundModes,
    Dictionary<string, DefInfo> DefInfos
)
{
    public ClientInitDataPacket ToNet() => new()
    {
        rwVersion = RwVersion,
        debugOnlySyncCmds = DebugOnlySyncCmds.ToArray(),
        hostOnlySyncCmds = HostOnlySyncCmds.ToArray(),
        modCtorRoundMode = RoundModes.Item1,
        staticCtorRoundMode = RoundModes.Item2,
        defInfos = DefInfos.Select(kv => new KeyedDefInfo
            { name = kv.Key, count = kv.Value.count, hash = kv.Value.hash }).ToArray(),
        rawData = RawData
    };

    public static ServerInitData FromNet(ClientInitDataPacket packet) => new(
        packet.rawData, packet.rwVersion,
        packet.debugOnlySyncCmds.ToHashSet(),
        packet.hostOnlySyncCmds.ToHashSet(),
        (packet.modCtorRoundMode, packet.staticCtorRoundMode),
        packet.defInfos.ToDictionary(info => info.name,
            info => new DefInfo { count = info.count, hash = info.hash }));
}
