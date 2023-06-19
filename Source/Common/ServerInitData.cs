using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Common;

public record ServerInitData(
    byte[] RawData,
    string RwVersion,
    HashSet<int> DebugOnlySyncCmds,
    HashSet<int> HostOnlySyncCmds,
    Dictionary<string, DefInfo> DefInfos
)
{
    public static ServerInitData Deserialize(ByteReader data)
    {
        var joinData = new ServerInitData
        (
            data.ReadPrefixedBytes()!,
            data.ReadString(),
            data.ReadPrefixedInts().ToHashSet(),
            data.ReadPrefixedInts().ToHashSet(),
            new Dictionary<string, DefInfo>()
        );

        var defInfoCount = data.ReadInt32();
        while (defInfoCount-- > 0)
            joinData.DefInfos[data.ReadString()] = new DefInfo { count = data.ReadInt32(), hash = data.ReadInt32() };

        return joinData;
    }
}
