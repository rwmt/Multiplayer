namespace Multiplayer.Common
{
    public enum MpDisconnectReason : byte
    {
        GenericKeyed,
        Protocol,
        UsernameLength,
        UsernameChars,
        UsernameAlreadyOnline,
        Generic,
        ServerClosed,
        ServerFull,
        Kick,
        ClientLeft,
        Throttled,
        NetFailed,
        ConnectingFailed,
        ServerPacketRead,
        Internal,
        ServerStarting,
        BadGamePassword
    }

}
