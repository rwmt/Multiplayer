using System;
using HarmonyLib;
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

    public record PacketHandlerInfo(FastInvokeHandler Method, bool Fragment);
}
