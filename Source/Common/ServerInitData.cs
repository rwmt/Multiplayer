using System.Collections.Generic;
using System.Linq;

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
    public static byte[] Serialize(ServerInitData data)
    {
        return ByteWriter.GetBytes(
            data.RawData,
            data.RwVersion,
            data.DebugOnlySyncCmds.ToList(),
            data.HostOnlySyncCmds.ToList(),
            data.RoundModes,
            data.DefInfos.Select(p => (p.Key, p.Value.count, p.Value.hash)).ToList()
        );
    }

    public static ServerInitData Deserialize(ByteReader data)
    {
        var joinData = new ServerInitData
        (
            data.ReadPrefixedBytes()!,
            data.ReadString(),
            data.ReadPrefixedInts().ToHashSet(),
            data.ReadPrefixedInts().ToHashSet(),
            ((RoundModeEnum)data.ReadInt32(), (RoundModeEnum)data.ReadInt32()),
            new Dictionary<string, DefInfo>()
        );

        var defInfoCount = data.ReadInt32();
        while (defInfoCount-- > 0)
            joinData.DefInfos[data.ReadString()] = new DefInfo { count = data.ReadInt32(), hash = data.ReadInt32() };

        return joinData;
    }
}
