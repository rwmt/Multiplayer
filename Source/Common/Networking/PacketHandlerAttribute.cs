using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Multiplayer.Common
{
    [MeansImplicitUse]
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
}
