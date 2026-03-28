namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_BootstrapSettingsUploadStart)]
public record struct ClientBootstrapSettingsPacket(ServerSettings settings) : IPacket
{
    public ServerSettings settings = settings;

    public void Bind(PacketBuffer buf)
    {
        ServerSettingsPacketBinder.Bind(buf, ref settings);
    }
}

internal static class ServerSettingsPacketBinder
{
    public static void Bind(PacketBuffer buf, ref ServerSettings settings)
    {
        settings ??= new ServerSettings();

        buf.Bind(ref settings.gameName, maxLength: 256);
        buf.Bind(ref settings.directAddress, maxLength: 256);
        buf.Bind(ref settings.maxPlayers);
        buf.Bind(ref settings.autosaveInterval);
        buf.BindEnum(ref settings.autosaveUnit);
        buf.Bind(ref settings.steam);
        buf.Bind(ref settings.direct);
        buf.Bind(ref settings.lan);
        buf.Bind(ref settings.arbiter);
        buf.Bind(ref settings.asyncTime);
        buf.Bind(ref settings.multifaction);
        buf.Bind(ref settings.debugMode);
        buf.Bind(ref settings.desyncTraces);
        buf.Bind(ref settings.syncConfigs);
        buf.BindEnum(ref settings.autoJoinPoint);
        buf.BindEnum(ref settings.devModeScope);
        buf.Bind(ref settings.hasPassword);
        buf.Bind(ref settings.password, maxLength: 256);
        buf.BindEnum(ref settings.pauseOnLetter);
        buf.Bind(ref settings.pauseOnJoin);
        buf.Bind(ref settings.pauseOnDesync);
        buf.BindEnum(ref settings.timeControl);
    }
}