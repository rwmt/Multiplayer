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
    private static string? pendingFileName;
    private static int pendingLength;
    private static byte[]? pendingZipBytes;

    public override void StartState()
    {
        if (!Server.BootstrapMode)
        {
            connection.ChangeState(ConnectionStateEnum.ServerJoining);
            return;
        }

        if (configuratorUsername != null && configuratorUsername != connection.username)
        {
            connection.Send(new ServerBootstrapPacket(true, SettingsMissing(), SaveMissing()));
            return;
        }

        configuratorUsername = connection.username;
        connection.Send(new ServerBootstrapPacket(true, SettingsMissing(), SaveMissing()));

        var savePath = Path.Combine(AppContext.BaseDirectory, "save.zip");
        if (SettingsMissing())
            ServerLog.Log($"Bootstrap: configurator '{connection.username}' connected. Waiting for settings.toml upload.");
        else if (!File.Exists(savePath))
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

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        var tempPath = settingsPath + ".tmp";

        TomlSettingsCommon.Save(packet.settings, tempPath);

        if (File.Exists(settingsPath))
            File.Delete(settingsPath);

        File.Move(tempPath, settingsPath);
        ServerLog.Log("Bootstrap: wrote settings.toml. Waiting for save upload.");
        connection.Send(new ServerBootstrapPacket(true, false, true));
    }

    [TypedPacketHandler]
    public void HandleSaveStart(ClientBootstrapSaveStartPacket packet)
    {
        if (!IsConfigurator())
            return;

        if (SettingsMissing())
            throw new PacketReadException("Bootstrap requires settings.toml before save.zip upload");

        if (packet.length <= 0)
            throw new PacketReadException("Bootstrap upload has invalid length");

        pendingFileName = packet.fileName;
        pendingLength = packet.length;
        pendingZipBytes = null;

        ServerLog.Log($"Bootstrap: upload start '{pendingFileName}' ({pendingLength} bytes)");
    }

    [TypedPacketHandler]
    public void HandleSaveData(ClientBootstrapSaveDataPacket packet)
    {
        if (!IsConfigurator())
            return;

        if (pendingZipBytes == null)
        {
            pendingZipBytes = packet.data;
        }
        else
        {
            var oldLength = pendingZipBytes.Length;
            var combined = new byte[oldLength + packet.data.Length];
            Buffer.BlockCopy(pendingZipBytes, 0, combined, 0, oldLength);
            Buffer.BlockCopy(packet.data, 0, combined, oldLength, packet.data.Length);
            pendingZipBytes = combined;
        }
    }

    [TypedPacketHandler]
    public void HandleSaveEnd(ClientBootstrapSaveEndPacket packet)
    {
        if (!IsConfigurator())
            return;

        if (SettingsMissing())
            throw new PacketReadException("Bootstrap requires settings.toml before save.zip upload");

        if (pendingZipBytes == null)
            throw new PacketReadException("Bootstrap upload finish without data");

        if (pendingLength > 0 && pendingZipBytes.Length != pendingLength)
            ServerLog.Log($"Bootstrap: warning - expected {pendingLength} bytes but got {pendingZipBytes.Length}");

        using var hasher = SHA256.Create();
        var actualHash = hasher.ComputeHash(pendingZipBytes);
        if (packet.sha256Hash != null && packet.sha256Hash.Length > 0 && !actualHash.SequenceEqual(packet.sha256Hash))
            throw new PacketReadException($"Bootstrap upload hash mismatch. expected={packet.sha256Hash.ToHexString()} actual={actualHash.ToHexString()}");

        var targetPath = Path.Combine(AppContext.BaseDirectory, "save.zip");
        var tempPath = targetPath + ".tmp";
        File.WriteAllBytes(tempPath, pendingZipBytes);
        if (File.Exists(targetPath))
            File.Delete(targetPath);
        File.Move(tempPath, targetPath);

        ServerLog.Log("Bootstrap: wrote save.zip. Configuration complete; stopping server.");

        foreach (var player in Server.JoinedPlayers.ToList())
            player.conn.Send(new ServerDisconnectPacket { reason = MpDisconnectReason.BootstrapCompleted, data = Array.Empty<byte>() });

        ResetUploadState();
        Server.running = false;
        Server.TryStop();
    }

    [PacketHandler(Packets.Client_Cursor)]
    public void HandleCursor(ByteReader data)
    {
        data.ReadByte();
        data.ReadByte();
    }

    private static bool SettingsMissing() => !File.Exists(Path.Combine(AppContext.BaseDirectory, "settings.toml"));

    private static bool SaveMissing() => !File.Exists(Path.Combine(AppContext.BaseDirectory, "save.zip"));

    private bool IsConfigurator() => configuratorUsername == connection.username;

    private static void ResetUploadState()
    {
        configuratorUsername = null;
        pendingFileName = null;
        pendingLength = 0;
        pendingZipBytes = null;
    }
}