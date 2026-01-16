using System;
using System.IO;
using System.Linq;
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
    // Only one configurator at a time; track by username to survive reconnections
    private static string? configuratorUsername;

    // TOML bytes received from client (will be parsed and saved as settings.toml)
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

        // If a different configurator is already active, keep this connection idle
        if (configuratorUsername != null && configuratorUsername != connection.username)
        {
            // Still tell them we're in bootstrap, so clients can show a helpful UI.
            var settingsMissing = !File.Exists(Path.Combine(AppContext.BaseDirectory, "settings.toml"));
            connection.Send(new ServerBootstrapPacket(true, settingsMissing));
            return;
        }

        // This is the configurator (either new or reconnecting)
        configuratorUsername = connection.username;
        var settingsMissing2 = !File.Exists(Path.Combine(AppContext.BaseDirectory, "settings.toml"));
        connection.Send(new ServerBootstrapPacket(true, settingsMissing2));

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        var savePath = Path.Combine(AppContext.BaseDirectory, "save.zip");

        if (!File.Exists(settingsPath))
            ServerLog.Log($"Bootstrap: configurator '{connection.username}' connected. Waiting for 'settings.toml' upload...");
        else if (!File.Exists(savePath))
            ServerLog.Log($"Bootstrap: configurator '{connection.username}' connected. settings.toml already present; waiting for 'save.zip' upload...");
        else
            ServerLog.Log($"Bootstrap: configurator '{connection.username}' connected. All files already present; waiting for shutdown.");
    }

    public override void OnDisconnect()
    {
        if (configuratorUsername == connection.username)
        {
            ServerLog.Log("Bootstrap: configurator disconnected; returning to waiting state.");
            ResetUploadState();
        }
    }

    [TypedPacketHandler]
    public void HandleSettingsUpload(ClientBootstrapSettingsUploadDataPacket packet)
    {
        if (!IsConfigurator())
            return;

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
        if (File.Exists(settingsPath))
        {
            ServerLog.Log("Bootstrap: settings.toml already exists; ignoring settings upload.");
            return;
        }

        if (packet.data == null || packet.data.Length == 0)
            throw new PacketReadException("Bootstrap settings upload received empty data");

        try
        {
            // Store TOML bytes; the Server project will parse and finalize them
            pendingSettingsBytes = packet.data;
            ServerLog.Log($"Bootstrap: ServerSettings TOML received ({packet.data.Length} bytes)");
            
            // Immediately persist to file since we have the complete TOML
            var tempPath = settingsPath + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllBytes(tempPath, pendingSettingsBytes);
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
            File.Move(tempPath, settingsPath);
            
            ServerLog.Log($"Bootstrap: ServerSettings saved to {settingsPath}");
        }
        catch (Exception e)
        {
            ServerLog.Error($"Bootstrap: Failed to process ServerSettings: {e}");
            throw;
        }
    }

    [TypedPacketHandler]
    public void HandleSaveStart(ClientBootstrapSaveStartPacket packet)
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
    public void HandleSaveData(ClientBootstrapSaveDataPacket packet)
    {
        if (!IsConfigurator())
            return;

        // Accumulate fragmented upload data
        if (pendingZipBytes == null)
        {
            pendingZipBytes = packet.data;
        }
        else
        {
            // Append new chunk to existing data
            var oldLen = pendingZipBytes.Length;
            var newChunk = packet.data;
            var combined = new byte[oldLen + newChunk.Length];
            Buffer.BlockCopy(pendingZipBytes, 0, combined, 0, oldLen);
            Buffer.BlockCopy(newChunk, 0, combined, oldLen, newChunk.Length);
            pendingZipBytes = combined;
        }
        
        ServerLog.Log($"Bootstrap: upload data received ({packet.data?.Length ?? 0} bytes, total: {pendingZipBytes?.Length ?? 0}/{pendingLength})");
    }

    [TypedPacketHandler]
    public void HandleSaveEnd(ClientBootstrapSaveEndPacket packet)
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

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var actualHash = sha256.ComputeHash(pendingZipBytes);
        if (packet.sha256Hash != null && packet.sha256Hash.Length > 0 && !actualHash.SequenceEqual(packet.sha256Hash))
        {
            throw new PacketReadException($"Bootstrap upload hash mismatch. expected={packet.sha256Hash.ToHexString()} actual={actualHash.ToHexString()}");
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
        Server.SendToPlaying(new ServerDisconnectPacket { reason = MpDisconnectReason.BootstrapCompleted, data = Array.Empty<byte>() });
        foreach (var p in Server.playerManager.Players.ToArray())
            p.conn.Close(MpDisconnectReason.ServerClosed);

        // Stop the server loop; an external supervisor should restart.
        Server.running = false;
    }

    private bool IsConfigurator() => configuratorUsername == connection.username;

    private static void ResetUploadState()
    {
        pendingSettingsBytes = null;
        pendingFileName = null;
        pendingLength = 0;
        pendingZipBytes = null;
        configuratorUsername = null;
    }
}
