namespace Multiplayer.Common
{
    public enum Packets : byte
    {
        CLIENT_USERNAME,
        CLIENT_REQUEST_WORLD,
        CLIENT_WORLD_LOADED,
        CLIENT_COMMAND,
        CLIENT_AUTOSAVED_DATA,
        CLIENT_ENCOUNTER_REQUEST,
        CLIENT_ID_BLOCK_REQUEST,
        CLIENT_CHAT,
        CLIENT_KEEP_ALIVE,
        CLIENT_STEAM_REQUEST,
        CLIENT_DEBUG,

        SERVER_WORLD_DATA,
        SERVER_COMMAND,
        SERVER_MAP_RESPONSE,
        SERVER_NOTIFICATION,
        SERVER_TIME_CONTROL,
        SERVER_CHAT,
        SERVER_PLAYER_LIST,
        SERVER_KEEP_ALIVE,
        SERVER_STEAM_ACCEPT,
        SERVER_DISCONNECT_REASON,
        SERVER_DEBUG,

        Count
    }

    public enum ConnectionStateEnum
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
