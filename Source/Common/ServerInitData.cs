using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

public record ServerInitData(
    byte[] RawData,
    bool IncludeConfigs,
    string RwVersion,
    string Language,
    HashSet<int> DebugOnlySyncCmds,
    HashSet<int> HostOnlySyncCmds,
    (RoundModeEnum, RoundModeEnum) RoundModes,
    Dictionary<string, DefInfo> DefInfos
)
{
    public static ServerInitData FromNet(ClientInitDataPacket packet) => new(
        packet.rawMods, packet.includeConfigs, packet.rwVersion, packet.language,
        packet.debugOnlySyncCmds.ToHashSet(),
        packet.hostOnlySyncCmds.ToHashSet(),
        (packet.modCtorRoundMode, packet.staticCtorRoundMode),
        packet.defInfos.ToDictionary(info => info.name,
            info => new DefInfo { count = info.count, hash = info.hash }));
}
