namespace Multiplayer.Common
{
    public enum Packets : byte
    {
        Client_Defs,
        Client_Username,
        Client_RequestWorld,
        Client_WorldReady,
        Client_Command,
        Client_AutosavedData,
        Client_IdBlockRequest,
        Client_Chat,
        Client_KeepAlive,
        Client_SteamRequest,
        Client_SyncInfo,
        Client_Cursor,
        Client_Desynced,
        Client_Pause,
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
        Server_SyncInfo,
        Server_Cursor,
        Server_Pause,
        Server_Debug,

        Count,
        Special_Steam_Disconnect
    }

    public enum ConnectionStateEnum : byte
    {
        ClientJoining,
        ClientPlaying,
        ClientSteam,

        ServerJoining,
        ServerPlaying,
        ServerSteam, // unused

        Count,
        Disconnected
    }
}
