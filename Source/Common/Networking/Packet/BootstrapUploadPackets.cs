using System;

namespace Multiplayer.Common.Networking.Packet;

/// <summary>
/// Upload raw TOML bytes for settings configuration.
/// The TOML is generated from ServerSettings.ExposeData() on the client
/// and parsed back to ServerSettings on the server.
/// This packet can be fragmented.
/// </summary>
[PacketDefinition(Packets.Client_BootstrapSettingsUploadData, allowFragmented: true)]
public record struct ClientBootstrapSettingsUploadDataPacket(byte[] data) : IPacket
{
    public byte[] data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.BindBytes(ref data, maxLength: -1);
    }
}

/// <summary>
/// Upload start metadata for bootstrap configuration.
/// The client will send exactly one file: a pre-built save.zip (server format).
/// </summary>
[PacketDefinition(Packets.Client_BootstrapUploadStart)]
public record struct ClientBootstrapSaveStartPacket(string fileName, int length) : IPacket
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
public record struct ClientBootstrapSaveDataPacket(byte[] data) : IPacket
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
public record struct ClientBootstrapSaveEndPacket(byte[] sha256Hash) : IPacket
{
    public byte[] sha256Hash = sha256Hash;

    public void Bind(PacketBuffer buf)
    {
        buf.BindBytes(ref sha256Hash, maxLength: 32);
    }
}
