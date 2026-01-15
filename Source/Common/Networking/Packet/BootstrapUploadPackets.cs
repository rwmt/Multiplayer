using System;

namespace Multiplayer.Common.Networking.Packet;

/// <summary>
/// Upload start metadata for bootstrap settings configuration.
/// The client may send exactly one file: settings.toml.
/// </summary>
    [PacketDefinition(Packets.Client_BootstrapSettingsUploadStart)]
    public record struct ClientBootstrapSettingsStartPacket(int length) : IPacket
{
    public int length = length;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref length);
    }
}

/// <summary>
/// Upload raw bytes for settings.toml.
/// This packet can be fragmented.
/// </summary>
[PacketDefinition(Packets.Client_BootstrapSettingsUploadData, allowFragmented: true)]
public record struct ClientBootstrapSettingsDataPacket(byte[] data) : IPacket
{
    public byte[] data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.BindBytes(ref data, maxLength: -1);
    }
}

/// <summary>
/// Notify the server the settings.toml upload has completed.
/// </summary>
[PacketDefinition(Packets.Client_BootstrapSettingsUploadFinish)]
public record struct ClientBootstrapSettingsEndPacket(string sha256Hex) : IPacket
{
    public string sha256Hex = sha256Hex;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref sha256Hex);
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
public record struct ClientBootstrapSaveEndPacket(string sha256Hex) : IPacket
{
    public string sha256Hex = sha256Hex;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref sha256Hex);
    }
}
