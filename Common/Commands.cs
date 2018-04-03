using System;
using System.Collections.Generic;
using System.Reflection;

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
        SETUP_FACTION,
        [GlobalScope]
        GLOBAL_ID_BLOCK,

        // Map scope
        MAP_TIME_SPEED,
        MAP_FACTION_DATA,
        MAP_ID_BLOCK,
        DRAFT_PAWN,
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
        public readonly int mapId;
        public readonly byte[] data;

        public ScheduledCommand(CommandType type, int ticks, int mapId, byte[] data)
        {
            this.type = type;
            this.ticks = ticks;
            this.mapId = mapId;
            this.data = data;
        }

        private static readonly HashSet<CommandType> globalCmds = new HashSet<CommandType>();

        static ScheduledCommand()
        {
            foreach (FieldInfo field in typeof(CommandType).GetFields(BindingFlags.Static | BindingFlags.Public))
                if (Attribute.GetCustomAttribute(field, typeof(GlobalScopeAttribute)) != null)
                    globalCmds.Add((CommandType)field.GetValue(null));
        }

        public static bool IsCommandGlobal(CommandType cmd)
        {
            return globalCmds.Contains(cmd);
        }
    }
}
