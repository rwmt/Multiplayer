using Multiplayer.Common;

namespace Multiplayer.Client.Saving
{
    public interface IHasSemiPersistentData
    {
        void WriteSemiPersistent(ByteWriter writer);
        void ReadSemiPersistent(ByteReader reader);
    }
}
