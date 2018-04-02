using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Multiplayer.Common
{
    public class GlobalScopeAttribute : Attribute
    {
    }

    public enum CommandType
    {
        [GlobalScope]
        WORLD_TIME_SPEED,
        [GlobalScope]
        AUTOSAVE,
        [GlobalScope]
        NEW_FACTION,

        // Map scope
        MAP_TIME_SPEED,
        MAP_ID_BLOCK,
        DRAFT,
        FORBID,
        DESIGNATOR,
        ORDER_JOB,
        DELETE_ZONE,
        SPAWN_PAWN
    }

    public class ScheduledCommand
    {
        public readonly CommandType type;
        public readonly int ticks;
        public readonly byte[] data;

        public ScheduledCommand(CommandType type, int ticks, byte[] data)
        {
            this.ticks = ticks;
            this.type = type;
            this.data = data;
        }
    }
}
