using System;
using HarmonyLib;
using JetBrains.Annotations;

namespace Multiplayer.Common
{
    [MeansImplicitUse]
    public class PacketHandlerAttribute(Packets packet, bool allowFragmented = false) : Attribute
    {
        public readonly Packets packet = packet;
        public readonly bool allowFragmented = allowFragmented;
    }

    public record PacketHandlerInfo(FastInvokeHandler Method, bool Fragment);
}
