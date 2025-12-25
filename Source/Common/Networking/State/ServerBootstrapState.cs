using System;
using System.IO;
using System.Security.Cryptography;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

/// <summary>
/// Server state used when the server is started in bootstrap mode (no save loaded).
/// It waits for a configuration client to upload a server-formatted save.zip.
/// Once received, the server writes it to disk and then disconnects all clients and stops,
/// so an external supervisor can restart it in normal mode.
/// </summary>
public class ServerBootstrapState(ConnectionBase conn) : MpConnectionState(conn)
{
    // Only one configurator at a time.
    private static int? configuratorPlayerId;

    private const int MaxSettingsTomlBytes = 64 * 1024;

    // Settings upload (settings.toml)
    private static string? pendingSettingsFileName;
    private static int pendingSettingsLength;
    private static byte[]? pendingSettingsBytes;

    // Save upload (save.zip)
    private static string? pendingFileName;
    private static int pendingLength;
    private static byte[]? pendingZipBytes;

    public override void StartState()
    {
        // If we're not actually in bootstrap mode anymore, fall back.
        if (!Server.BootstrapMode)
        {
            connection.ChangeState(ConnectionStateEnum.ServerJoining);
            return;
        }

        // If someone already is configuring, keep this connection idle.
        if (configuratorPlayerId != null && configuratorPlayerId != Player.id)
        {
            // Still tell them we're in bootstrap, so clients can show a helpful UI.
            connection.Send(new ServerBootstrapPacket(true));
            return;
        }

        configuratorPlayerId = Player.id;
        connection.Send(new ServerBootstrapPacket(true));

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        var savePath = Path.Combine(AppContext.BaseDirectory, "save.zip");

        if (!File.Exists(settingsPath))
            ServerLog.Log($"Bootstrap: configurator connected (playerId={Player.id}). Waiting for 'settings.toml' upload...");
        else if (!File.Exists(savePath))
            ServerLog.Log($"Bootstrap: configurator connected (playerId={Player.id}). settings.toml already present; waiting for 'save.zip' upload...");
        else
            ServerLog.Log($"Bootstrap: configurator connected (playerId={Player.id}). All files already present; waiting for shutdown.");
    }

    public override void OnDisconnect()
    {
        if (configuratorPlayerId == Player.id)
        {
            ServerLog.Log("Bootstrap: configurator disconnected; returning to waiting state.");
            ResetUploadState();
            configuratorPlayerId = null;
        }
    }

    [TypedPacketHandler]
    public void HandleSettingsUploadStart(ClientBootstrapSettingsUploadStartPacket packet)
    {
        if (!IsConfigurator())
            return;

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        if (File.Exists(settingsPath))
        {
            ServerLog.Log("Bootstrap: settings.toml already exists; ignoring settings upload start.");
            return;
        }

        if (packet.length <= 0 || packet.length > MaxSettingsTomlBytes)
            throw new PacketReadException($"Bootstrap settings upload has invalid length ({packet.length})");

        pendingSettingsFileName = packet.fileName;
        pendingSettingsLength = packet.length;
        pendingSettingsBytes = null;

        ServerLog.Log($"Bootstrap: settings upload start '{pendingSettingsFileName}' ({pendingSettingsLength} bytes)");
    }

    [TypedPacketHandler]
    public void HandleSettingsUploadData(ClientBootstrapSettingsUploadDataPacket packet)
    {
        if (!IsConfigurator())
            return;

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        if (File.Exists(settingsPath))
            return;

        pendingSettingsBytes = packet.data;
        ServerLog.Log($"Bootstrap: settings upload data received ({pendingSettingsBytes?.Length ?? 0} bytes)");
    }

    [TypedPacketHandler]
    public void HandleSettingsUploadFinish(ClientBootstrapSettingsUploadFinishPacket packet)
    {
        if (!IsConfigurator())
            return;

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        if (File.Exists(settingsPath))
        {
            ServerLog.Log("Bootstrap: settings.toml already exists; ignoring settings upload finish.");
            return;
        }

        if (pendingSettingsBytes == null)
            throw new PacketReadException("Bootstrap settings upload finish without data");

        if (pendingSettingsLength > 0 && pendingSettingsBytes.Length != pendingSettingsLength)
            ServerLog.Log($"Bootstrap: warning - expected {pendingSettingsLength} settings bytes but got {pendingSettingsBytes.Length}");

        var actualHash = ComputeSha256Hex(pendingSettingsBytes);
        if (!string.IsNullOrWhiteSpace(packet.sha256Hex) &&
            !actualHash.Equals(packet.sha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new PacketReadException($"Bootstrap settings upload hash mismatch. expected={packet.sha256Hex} actual={actualHash}");
        }

        // Persist settings.toml
        var tempPath = settingsPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllBytes(tempPath, pendingSettingsBytes);
        if (File.Exists(settingsPath))
            File.Delete(settingsPath);
        File.Move(tempPath, settingsPath);

        ServerLog.Log($"Bootstrap: wrote '{settingsPath}'. Waiting for save.zip upload...");

        pendingSettingsFileName = null;
        pendingSettingsLength = 0;
        pendingSettingsBytes = null;
    }

    [TypedPacketHandler]
    public void HandleUploadStart(ClientBootstrapUploadStartPacket packet)
    {
        if (!IsConfigurator())
            return;

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        if (!File.Exists(settingsPath))
            throw new PacketReadException("Bootstrap requires settings.toml before save.zip");

        if (packet.length <= 0)
            throw new PacketReadException("Bootstrap upload has invalid length");

        pendingFileName = packet.fileName;
        pendingLength = packet.length;
        pendingZipBytes = null;

        ServerLog.Log($"Bootstrap: upload start '{pendingFileName}' ({pendingLength} bytes)");
    }

    [TypedPacketHandler]
    public void HandleUploadData(ClientBootstrapUploadDataPacket packet)
    {
        if (!IsConfigurator())
            return;

        // Expect the full zip bytes in this packet (delivered fragmented).
        pendingZipBytes = packet.data;
        ServerLog.Log($"Bootstrap: upload data received ({pendingZipBytes?.Length ?? 0} bytes)");
    }

    [TypedPacketHandler]
    public void HandleUploadFinish(ClientBootstrapUploadFinishPacket packet)
    {
        if (!IsConfigurator())
            return;

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        if (!File.Exists(settingsPath))
            throw new PacketReadException("Bootstrap requires settings.toml before save.zip");

        if (pendingZipBytes == null)
            throw new PacketReadException("Bootstrap upload finish without data");

        if (pendingLength > 0 && pendingZipBytes.Length != pendingLength)
            ServerLog.Log($"Bootstrap: warning - expected {pendingLength} bytes but got {pendingZipBytes.Length}");

        var actualHash = ComputeSha256Hex(pendingZipBytes);
        if (!string.IsNullOrWhiteSpace(packet.sha256Hex) &&
            !actualHash.Equals(packet.sha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new PacketReadException($"Bootstrap upload hash mismatch. expected={packet.sha256Hex} actual={actualHash}");
        }

        // Persist save.zip
        var targetPath = Path.Combine(AppContext.BaseDirectory, "save.zip");
        var tempPath = targetPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(tempPath, pendingZipBytes);
        if (File.Exists(targetPath))
            File.Delete(targetPath);
        File.Move(tempPath, targetPath);

        ServerLog.Log($"Bootstrap: wrote '{targetPath}'. Configuration complete; disconnecting clients and stopping.");

        // Notify and disconnect all clients.
        Server.SendToPlaying(new ServerBootstrapCompletePacket("Server configured. The server will now shut down; please restart it manually to start normally."));
        foreach (var p in Server.playerManager.Players.ToArray())
            p.conn.Close(MpDisconnectReason.ServerClosed);

        // Stop the server loop; an external supervisor should restart.
        Server.running = false;
    }

    private bool IsConfigurator() => configuratorPlayerId == Player.id;

    private static void ResetUploadState()
    {
        pendingSettingsFileName = null;
        pendingSettingsLength = 0;
        pendingSettingsBytes = null;

        pendingFileName = null;
        pendingLength = 0;
        pendingZipBytes = null;
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        return ToHexString(hash);
    }

    private static string ToHexString(byte[] bytes)
    {
        const string hex = "0123456789ABCDEF";
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i * 2] = hex[b >> 4];
            chars[i * 2 + 1] = hex[b & 0x0F];
        }
        return new string(chars);
    }
}
