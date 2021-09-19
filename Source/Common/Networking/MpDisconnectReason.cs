namespace Multiplayer.Common
{
    public enum MpDisconnectReason : byte
    {
        GenericKeyed,
        Protocol,
        Defs,
        UsernameLength,
        UsernameChars,
        UsernameAlreadyOnline,
        ServerClosed,
        ServerFull,
        Kick,
        ClientLeft,
        Throttled,
        NetFailed,
        ConnectingFailed,
        ServerPacketRead,
        Internal,
        Generic,
    }

}
