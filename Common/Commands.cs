namespace Multiplayer.Common
{
    public enum CommandType
    {
        // Global scope
        WORLD_TIME_SPEED,
        AUTOSAVE,
        SETUP_FACTION,
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
    }
}
