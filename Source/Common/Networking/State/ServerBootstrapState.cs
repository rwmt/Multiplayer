using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Multiplayer.Common.Networking.Packet;
using Multiplayer.Common.Util;

namespace Multiplayer.Common;

[PacketHandlerClass]
public class ServerBootstrapState(ConnectionBase connection) : MpConnectionState(connection)
{
    private static string? configuratorUsername;

    private static readonly string SavePath = Path.Combine(AppContext.BaseDirectory, "save.zip");
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");

    public override void StartState()
    {
        if (!Server.BootstrapMode)
        {
            connection.ChangeState(ConnectionStateEnum.ServerJoining);
            return;
        }

        connection.Send(new ServerBootstrapPacket(true, SettingsMissing(), SaveMissing()));
        if (configuratorUsername != null && configuratorUsername != connection.username)
        {
            return;
        }

        configuratorUsername = connection.username;
        if (SettingsMissing())
            ServerLog.Log($"Bootstrap: configurator '{connection.username}' connected. Waiting for settings.toml upload.");
        else if (SaveMissing())
            ServerLog.Log($"Bootstrap: configurator '{connection.username}' connected. Waiting for save.zip upload.");
        else
            ServerLog.Log($"Bootstrap: configurator '{connection.username}' connected. All files already exist.");
    }

    public override void OnDisconnect()
    {
        if (configuratorUsername == connection.username)
            ResetUploadState();
    }

    [TypedPacketHandler]
    public void HandleSettings(ClientBootstrapSettingsPacket packet)
    {
        if (!IsConfigurator())
            return;

        var tempPath = SettingsPath + ".tmp";

        TomlSettings.Save(packet.settings, tempPath);

        File.Delete(SettingsPath);
        File.Move(tempPath, SettingsPath);
        ServerLog.Log("Bootstrap: wrote settings.toml. Waiting for save upload.");
        connection.Send(new ServerBootstrapPacket(true, false, true));
    }

    [TypedPacketHandler]
    public void HandleSaveData(ClientBootstrapSaveDataPacket packet)
    {
        if (!IsConfigurator())
            return;

        if (SettingsMissing())
            throw new PacketReadException("Bootstrap requires settings.toml before save.zip upload");

        byte[] hash;
        using (var hasher = SHA256.Create())
        {
            hash = hasher.ComputeHash(packet.saveData);
        }

        if (packet.sha256Hash is { Length: > 0 } && !hash.SequenceEqual(packet.sha256Hash))
            throw new PacketReadException($"Bootstrap upload hash mismatch. " +
                                          $"expected={packet.sha256Hash.ToHexString()} actual={hash.ToHexString()}");

        var tempPath = SavePath + ".tmp";
        File.WriteAllBytes(tempPath, packet.saveData);

        File.Delete(SavePath);
        File.Move(tempPath, SavePath);

        ServerLog.Log("Bootstrap: wrote save.zip. Configuration complete; stopping server.");

        foreach (var player in Server.JoinedPlayers.ToList())
            player.conn.Send(new ServerDisconnectPacket { reason = MpDisconnectReason.BootstrapCompleted, data = Array.Empty<byte>() });

        ResetUploadState();
        Server.running = false;
        Server.TryStop();
    }

    [FragmentedPacketHandler(Packets.Client_BootstrapSave)]
    public void HandleWorldDataFragment(FragmentedPacket packet)
    {
        if (packet.ReceivedPartsCount == 1)
        {
            ServerLog.Log($"Bootstrap: upload start ({packet.ExpectedSize} bytes)");
        }
    }

    [PacketHandler(Packets.Client_Cursor)]
    public void HandleCursor(ByteReader data)
    {
        data.ReadByte();
        data.ReadByte();
    }

    private static bool SettingsMissing() => !File.Exists(SettingsPath);
    private static bool SaveMissing() => !File.Exists(SavePath);

    private bool IsConfigurator() => configuratorUsername == connection.username;

    private static void ResetUploadState() => configuratorUsername = null;
}
