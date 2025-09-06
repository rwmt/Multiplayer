using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Multiplayer.Common
{
    public abstract class MpConnectionState(ConnectionBase connection)
    {
        protected readonly ConnectionBase connection = connection;
        public bool alive = true;

        protected ServerPlayer Player => connection.serverPlayer;
        protected MultiplayerServer Server => MultiplayerServer.instance!;

        public virtual void StartState()
        {
        }

        public virtual void OnDisconnect()
        {
        }

        public virtual PacketHandlerInfo? GetPacketHandler(Packets id) =>
            packetHandlers[(int)connection.State, (int)id];

        public static Type[] stateImpls = new Type[(int)ConnectionStateEnum.Count];
        private static PacketHandlerInfo?[,] packetHandlers = new PacketHandlerInfo?[(int)ConnectionStateEnum.Count, (int)Packets.Count];

        public static void SetImplementation(ConnectionStateEnum state, Type type)
        {
            if (!type.IsSubclassOf(typeof(MpConnectionState))) return;

            stateImpls[(int)state] = type;

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var attr = method.GetAttribute<PacketHandlerAttribute>();
                if (attr != null) RegisterPacketHandler(state, method, attr);

                var attr2 = method.GetAttribute<FragmentedPacketHandlerAttribute>();
                if (attr2 != null) RegisterFragmentedPacketHandler(state, method, attr2);
            }

            for (var packetId = 0; packetId < packetHandlers.GetLength(1); packetId++)
            {
                var handlerInfo = packetHandlers[(int)state, packetId];
                if (handlerInfo is { Method: null })
                {
                    throw new Exception(
                        $"Packet handler for {state}:{(Packets)packetId} only has a handler for fragments!");
                }
            }
        }

        private static void RegisterPacketHandler(ConnectionStateEnum state, MethodInfo method, PacketHandlerAttribute attr)
        {
            var packetHandlerInfo = packetHandlers[(int)state, (int)attr.packet];
            if (packetHandlerInfo == null)
            {
                packetHandlers[(int)state, (int)attr.packet] =
                    new PacketHandlerInfo(CreateInvoker(attr.packet, method), attr.allowFragmented);
                return;
            }
            if (packetHandlerInfo.Method != null)
                throw new Exception($"Packet {state}:{attr.packet} already has a handler");

            if (!attr.allowFragmented && packetHandlerInfo.FragmentHandler != null)
                throw new Exception($"Packet {state}:{attr.packet} has a fragment handler despite not being allowed to");

            packetHandlers[(int)state, (int)attr.packet] = packetHandlerInfo with
            {
                Method = CreateInvoker(attr.packet, method), Fragment = attr.allowFragmented
            };
        }

        private static PacketHandlerInvoker CreateInvoker(Packets packet, MethodInfo handler)
        {
            if (handler.GetParameters().Length != 1 || handler.GetParameters()[0].ParameterType != typeof(ByteReader))
                throw new Exception($"Bad packet handler signature for {handler}");

            DynamicMethod invoker = new DynamicMethod($"PacketHandlerInvoker_{packet}_{handler.Name}", typeof(void), [typeof(object), typeof(ByteReader)]);
            var il = invoker.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, handler.DeclaringType ?? throw new InvalidOperationException());
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, handler);
            il.Emit(OpCodes.Ret);
            return (PacketHandlerInvoker)invoker.CreateDelegate(typeof(PacketHandlerInvoker));
        }

        private static void RegisterFragmentedPacketHandler(ConnectionStateEnum state, MethodInfo method, FragmentedPacketHandlerAttribute attr)
        {
            if (method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(FragmentedPacket))
                throw new Exception($"Bad packet handler signature for {method}");

            var packetHandlerInfo = packetHandlers[(int)state, (int)attr.packet];
            if (packetHandlerInfo == null)
            {
                packetHandlers[(int)state, (int)attr.packet] =
                    new PacketHandlerInfo(null!, false, MethodInvoker.GetHandler(method));
                return;
            }
            if (packetHandlerInfo.FragmentHandler != null)
                throw new Exception($"Packet {state}:{attr.packet} already has a fragment handler");

            if (!packetHandlerInfo.Fragment)
                throw new Exception($"Packet {state}:{attr.packet} has a fragment handler despite not being allowed to");

            packetHandlers[(int)state, (int)attr.packet] = packetHandlerInfo with
            {
                FragmentHandler = MethodInvoker.GetHandler(method)
            };
        }
    }

}
