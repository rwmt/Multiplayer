using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Multiplayer.Common
{
    public enum Packets
    {
        CLIENT_USERNAME,
        CLIENT_REQUEST_WORLD,
        CLIENT_WORLD_LOADED,
        CLIENT_COMMAND,
        CLIENT_AUTOSAVED_DATA,
        CLIENT_ENCOUNTER_REQUEST,
        CLIENT_MAP_RESPONSE,
        CLIENT_MAP_LOADED,
        CLIENT_ID_BLOCK_REQUEST,
        CLIENT_MAP_STATE_DEBUG,
        CLIENT_NEW_FACTION_RESPONSE,

        SERVER_WORLD_DATA,
        SERVER_COMMAND,
        SERVER_MAP_REQUEST,
        SERVER_MAP_RESPONSE,
        SERVER_NOTIFICATION,
        SERVER_NEW_ID_BLOCK,
        SERVER_TIME_CONTROL,
        SERVER_DISCONNECT_REASON,
        SERVER_NEW_FACTION_REQUEST
    }
}
