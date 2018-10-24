namespace Multiplayer.Common
{
    public enum Packets : byte
    {
        Client_Defs,
        Client_Username,
        Client_RequestWorld,
        Client_WorldLoaded,
        Client_Command,
        Client_AutosavedData,
        Client_IdBlockRequest,
        Client_Chat,
        Client_KeepAlive,
        Client_SteamRequest,
        Client_Debug,

        Server_DefsOK,
        Server_WorldData,
        Server_Command,
        Server_MapResponse,
        Server_Notification,
        Server_TimeControl,
        Server_Chat,
        Server_PlayerList,
        Server_KeepAlive,
        Server_SteamAccept,
        Server_DisconnectReason,
        Server_Debug,

        Count
    }

    public enum ConnectionStateEnum : byte
    {
        ClientJoining,
        ClientPlaying,
        ClientSteam,

        ServerJoining,
        ServerPlaying,
        ServerSteam,

        Count,
        Disconnected
    }
}
