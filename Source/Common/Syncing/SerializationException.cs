using System;

namespace Multiplayer.Client
{
    public class SerializationException : Exception
    {
        public SerializationException(string msg) : base(msg)
        {
        }
    }
}
