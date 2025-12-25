using System;

namespace Multiplayer.Common.Networking.Packet;

/// <summary>
/// Upload start metadata for bootstrap configuration.
/// The client will send exactly one file: a pre-built save.zip (server format).
/// </summary>
[PacketDefinition(Packets.Client_BootstrapUploadStart)]
public record struct ClientBootstrapUploadStartPacket(string fileName, int length) : IPacket
{
    public string fileName = fileName;
    public int length = length;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref fileName);
        buf.Bind(ref length);
    }
}

/// <summary>
/// Upload raw bytes for the save.zip.
/// This packet is expected to be delivered fragmented due to size.
/// </summary>
[PacketDefinition(Packets.Client_BootstrapUploadData, allowFragmented: true)]
public record struct ClientBootstrapUploadDataPacket(byte[] data) : IPacket
{
    public byte[] data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.BindBytes(ref data, maxLength: -1);
    }
}

/// <summary>
/// Notify the server the upload has completed.
/// </summary>
[PacketDefinition(Packets.Client_BootstrapUploadFinish)]
public record struct ClientBootstrapUploadFinishPacket(string sha256Hex) : IPacket
{
    public string sha256Hex = sha256Hex;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref sha256Hex);
    }
}

/// <summary>
/// Server informs connected clients that bootstrap configuration finished and it will restart.
/// </summary>
[PacketDefinition(Packets.Server_BootstrapComplete)]
public record struct ServerBootstrapCompletePacket(string message) : IPacket
{
    public string message = message;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref message);
    }
}
