using System;

namespace Multiplayer.Common
{
    public class PacketReadException : Exception
    {
        public PacketReadException(string msg) : base(msg)
        {
        }
    }
}
