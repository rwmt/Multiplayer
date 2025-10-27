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
    public class TypedPacketHandlerAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Class)]
    public class PacketHandlerClassAttribute(bool inheritHandlers = false) : Attribute
    {
        public readonly bool inheritHandlers = inheritHandlers;
    }

    [MeansImplicitUse]
    [AttributeUsage(AttributeTargets.Method)]
    public class FragmentedPacketHandlerAttribute(Packets packet) : Attribute
    {
        public readonly Packets packet = packet;
    }

    public delegate void PacketHandlerInvoker(object target, ByteReader data);

    public record PacketHandlerInfo(PacketHandlerInvoker Method, bool Fragment, FastInvokeHandler? FragmentHandler = null);
}
