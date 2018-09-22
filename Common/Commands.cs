using System;

namespace Multiplayer.Common
{
    public enum CommandType : byte
    {
        // Global scope
        WORLD_TIME_SPEED,
        AUTOSAVE,
        SETUP_FACTION,
        GLOBAL_ID_BLOCK,
        FACTION_ONLINE,
        FACTION_OFFLINE,

        // Mixed scope
        SYNC,

        // Map scope
        MAP_TIME_SPEED,
        CREATE_MAP_FACTION_DATA,
        MAP_ID_BLOCK,
        FORBID,
        DESIGNATOR,
        SPAWN_PAWN,
    }

    public class ScheduledCommand
    {
        public const int NoFaction = -1;
        public const int Global = -1;

        public readonly CommandType type;
        public readonly int ticks;
        public readonly int factionId;
        public readonly int mapId;
        public readonly byte[] data;

        // Client only, not serialized
        public bool issuedBySelf;

        public ScheduledCommand(CommandType type, int ticks, int factionId, int mapId, byte[] data)
        {
            this.type = type;
            this.ticks = ticks;
            this.factionId = factionId;
            this.mapId = mapId;
            this.data = data;
        }

        public byte[] GetBytes()
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(Convert.ToInt32(type));
            writer.WriteInt32(ticks);
            writer.WriteInt32(factionId);
            writer.WriteInt32(mapId);
            writer.WritePrefixedBytes(data);

            return writer.GetArray();
        }

        public static ScheduledCommand Deserialize(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt32();
            int ticks = data.ReadInt32();
            int factionId = data.ReadInt32();
            int mapId = data.ReadInt32();
            byte[] extraBytes = data.ReadPrefixedBytes();

            return new ScheduledCommand(cmd, ticks, factionId, mapId, extraBytes);
        }
    }
}
