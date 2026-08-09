using System.Collections.Generic;

namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_InitDataRequest)]
public record struct ServerInitDataRequestPacket(bool includeConfigs) : IPacket
{
    public bool includeConfigs = includeConfigs;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref includeConfigs);
    }
}

[PacketDefinition(Packets.Client_InitData, allowFragmented: true)]
public record struct ClientInitDataPacket : IPacket
{
    // 1 MiB limit — large mod lists can exceed the default 32 KiB BindRemaining limit
    private const int MaxRawDataLength = 1 << 20;

    public string rwVersion;
    public string language;
    public int[] debugOnlySyncCmds;
    public int[] hostOnlySyncCmds;
    public RoundModeEnum modCtorRoundMode;
    public RoundModeEnum staticCtorRoundMode;
    public KeyedDefInfo[] defInfos;
    public bool includeConfigs;
    public byte[] rawMods;

    public List<ModData> Mods
    {
        get => ModData.ListBinder.Deserialize(rawMods);
        set => rawMods = ModData.ListBinder.Serialize(value);
    }

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref rwVersion);
        buf.Bind(ref language);
        buf.Bind(ref debugOnlySyncCmds, BinderOf.Int());
        buf.Bind(ref hostOnlySyncCmds, BinderOf.Int());
        buf.BindEnum(ref modCtorRoundMode);
        buf.BindEnum(ref staticCtorRoundMode);
        buf.Bind(ref defInfos, BinderOf.Identity<KeyedDefInfo>());
        buf.Bind(ref includeConfigs);
        buf.BindRemaining(ref rawMods);
    }

    // Based on ContentSource but a byte, so smaller on the network and also doesn't use Verse
    // (which is unavailable in the standalone server)
    public enum ModSource : byte
    {
        Undefined,
        OfficialModsFolder,
        ModsFolder,
        SteamWorkshop,
    }

    public record struct ModData : IPacketBufferable
    {
        public string packageIdNonUnique;
        public string name;
        public ulong publishedFileId;
        public ModSource source;
        public List<ModFile> files;
        public ModConfig? config;

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref packageIdNonUnique);
            buf.Bind(ref name);
            buf.Bind(ref publishedFileId);
            buf.BindEnum(ref source);
            buf.Bind(ref files, BinderOf.Identity<ModFile>());
            buf.BindWith(ref config, ConfigBinder);
        }

        private static readonly Binder<ModConfig?> ConfigBinder = (PacketBuffer buf, ref ModConfig? modConfig) =>
        {
            if (buf.isWriting)
            {
                var present = modConfig.HasValue;
                buf.Bind(ref present);
                if (present)
                {
                    ModConfig config = modConfig.Value;
                    buf.Bind(ref config);
                }
            }
            else
            {
                var present = false;
                buf.Bind(ref present);
                if (present)
                {
                    ModConfig config = new();
                    buf.Bind(ref config);
                    modConfig = config;
                }
            }
        };

        public static Binder<List<ModData>> ListBinder =>
            BinderOf.List(BinderOf.Identity<ModData>()).Gzipped(MaxRawDataLength);
    }

    public record struct ModFile : IPacketBufferable
    {
        public string path;
        public int hash;

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref path);
            buf.Bind(ref hash);
        }
    }

    public record struct ModConfig : IPacketBufferable
    {
        public string fileName;
        public string contents;

        private const int MaxConfigContentLen = 8388608; // 8 MiB

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref fileName);
            buf.Bind(ref contents, maxLength:MaxConfigContentLen);
        }
    }
}
