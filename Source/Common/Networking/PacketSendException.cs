using System;

namespace Multiplayer.Common
{
    public class PacketSendException : Exception
    {
        public PacketSendException(string msg) : base(msg)
        {
        }
    }
}
