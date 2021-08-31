namespace Multiplayer.Common
{
    public class IdBlock
    {
        public int blockStart;
        public int blockSize;
        public int mapId;

        public int currentWithinBlock;
        public bool overflowHandled;

        public int Current => blockStart + currentWithinBlock;

        public IdBlock(int blockStart, int blockSize, int mapId = -1)
        {
            this.blockStart = blockStart;
            this.blockSize = blockSize;
            this.mapId = mapId;
        }

        public int NextId()
        {
            // Overflows should be handled by the caller
            currentWithinBlock++;
            return blockStart + currentWithinBlock;
        }

        public byte[] Serialize()
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(blockStart);
            writer.WriteInt32(blockSize);
            writer.WriteInt32(mapId);
            writer.WriteInt32(currentWithinBlock);

            return writer.ToArray();
        }

        public static IdBlock Deserialize(ByteReader data)
        {
            IdBlock block = new IdBlock(data.ReadInt32(), data.ReadInt32(), data.ReadInt32());
            block.currentWithinBlock = data.ReadInt32();
            return block;
        }
    }
}
