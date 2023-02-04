namespace Multiplayer.Common
{
    public enum Packets : byte
    {
        // Client_ means the origin is the client
        // Server_ means the origin is the server

        // Special
        Client_Protocol, // Must be zeroth for future proofing
        Server_SteamAccept, // Packet for the special Steam state, must be first

        // Joining
        Client_Username,
        Client_JoinData,
        Client_WorldRequest,

        // Playing
        Client_WorldReady,
        Client_Command,
        Client_WorldDataUpload,
        Client_IdBlockRequest,
        Client_Chat,
        Client_KeepAlive,
        Client_SteamRequest,
        Client_SyncInfo,
        Client_Cursor,
        Client_Desynced,
        Client_Freeze,
        Client_Debug,
        Client_Selected,
        Client_PingLocation,
        Client_Traces,
        Client_Autosaving,
        Client_RequestRejoin,
        Client_SetFaction,
        Client_FrameTime,

        // Joining
        Server_ProtocolOk,
        Server_UsernameOk,
        Server_JoinData,
        Server_WorldDataStart,
        Server_WorldData,

        // Playing
        Server_Command,
        Server_MapResponse,
        Server_Notification,
        Server_TimeControl,
        Server_Chat,
        Server_PlayerList,
        Server_KeepAlive,
        Server_SyncInfo,
        Server_Cursor,
        Server_Freeze,
        Server_Debug,
        Server_Selected,
        Server_PingLocation,
        Server_Traces,
        Server_CanRejoin,
        Server_SetFaction,

        Count,
        Special_Steam_Disconnect = 63 // Also the max packet id
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
