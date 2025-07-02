using System;
using HarmonyLib;
using JetBrains.Annotations;

namespace Multiplayer.Common
{
    [MeansImplicitUse]
    [AttributeUsage(AttributeTargets.Method)]
    public class PacketHandlerAttribute(Packets packet, bool allowFragmented = false) : Attribute
    {
        public readonly Packets packet = packet;
        public readonly bool allowFragmented = allowFragmented;
    }

    [MeansImplicitUse]
    [AttributeUsage(AttributeTargets.Method)]
    public class FragmentedPacketHandlerAttribute(Packets packet) : Attribute
    {
        public readonly Packets packet = packet;
    }

    public record PacketHandlerInfo(FastInvokeHandler Method, bool Fragment, FastInvokeHandler? FragmentHandler = null);
}
