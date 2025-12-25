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
        ServerLog.Log($"Bootstrap: configurator connected (playerId={Player.id}). Waiting for upload...");
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
    public void HandleUploadStart(ClientBootstrapUploadStartPacket packet)
    {
        if (!IsConfigurator())
            return;

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
