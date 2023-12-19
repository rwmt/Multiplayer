using Multiplayer.Common;

namespace Multiplayer.Client.Saving
{
    public interface IHasSessionData
    {
        void WriteSessionData(ByteWriter writer);
        void ReadSessionData(ByteReader reader);
    }
}
