using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Client;

public readonly record struct BootstrapServerState(bool Enabled, bool SettingsMissing, bool SaveMissing)
{
    public static BootstrapServerState None => new(false, false, false);

    public bool RequiresSettingsUpload => Enabled && SettingsMissing;
    public bool RequiresSaveUpload => Enabled && !SettingsMissing && SaveMissing;

    public static BootstrapServerState FromPacket(ServerBootstrapPacket packet) =>
        new(packet.bootstrap, packet.settingsMissing, packet.saveMissing);
}