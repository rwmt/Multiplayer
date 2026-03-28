using System;
using System.IO;
using Multiplayer.Common.Networking.Packet;
using Multiplayer.Common.Util;

namespace Multiplayer.Common;

[PacketHandlerClass]
public class ServerBootstrapState(ConnectionBase connection) : MpConnectionState(connection)
{
    private static string? configuratorUsername;

    public override void StartState()
    {
        if (!Server.BootstrapMode)
        {
            connection.ChangeState(ConnectionStateEnum.ServerJoining);
            return;
        }

        if (configuratorUsername != null && configuratorUsername != connection.username)
        {
            connection.Send(new ServerBootstrapPacket(true, SettingsMissing()));
            return;
        }

        configuratorUsername = connection.username;
        connection.Send(new ServerBootstrapPacket(true, SettingsMissing()));
        ServerLog.Log($"Bootstrap: configurator '{connection.username}' connected.");
    }

    public override void OnDisconnect()
    {
        if (configuratorUsername == connection.username)
            configuratorUsername = null;
    }

    [TypedPacketHandler]
    public void HandleSettings(ClientBootstrapSettingsPacket packet)
    {
        if (!IsConfigurator())
            return;

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        var tempPath = settingsPath + ".tmp";

        TomlSettingsCommon.Save(packet.settings, tempPath);

        if (File.Exists(settingsPath))
            File.Delete(settingsPath);

        File.Move(tempPath, settingsPath);
        ServerLog.Log("Bootstrap: wrote settings.toml. Waiting for save upload.");
        connection.Send(new ServerBootstrapPacket(true, false));
    }

    private static bool SettingsMissing() => !File.Exists(Path.Combine(AppContext.BaseDirectory, "settings.toml"));

    private bool IsConfigurator() => configuratorUsername == connection.username;
}