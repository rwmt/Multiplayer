using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Common
{
    public class PacketHandlerAttribute : Attribute
    {
        public readonly Packets packet;

        public PacketHandlerAttribute(Packets packet)
        {
            this.packet = packet;
        }
    }

    public class IsFragmentedAttribute : Attribute
    {
    }

    public record PacketHandlerInfo(MethodInfo Method, bool Fragment);

    public class PacketReadException : Exception
    {
        public PacketReadException(string msg) : base(msg)
        {
        }
    }

    public class PacketSendException : Exception
    {
        public PacketSendException(string msg) : base(msg)
        {
        }
    }
}
