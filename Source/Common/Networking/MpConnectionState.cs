using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Common.Networking.Packet;

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

        private static readonly Type[] StateImpls = new Type[(int)ConnectionStateEnum.Count];

        private static PacketHandlerInfo?[,] packetHandlers =
            new PacketHandlerInfo?[(int)ConnectionStateEnum.Count, (int)Packets.Count];

        public static ConnectionStateEnum GetStateEnumOf(MpConnectionState state)
        {
            var stateType = state.GetType();
            var index = Array.IndexOf(StateImpls, stateType);
            if (index == -1) throw new Exception($"Tried to get state enum of unrecognized connection state: {state} ({stateType})");
            return (ConnectionStateEnum)index;
        }

        public static MpConnectionState? CreateState(ConnectionStateEnum state, ConnectionBase conn) =>
            state == ConnectionStateEnum.Disconnected
                ? null
                : (MpConnectionState)Activator.CreateInstance(StateImpls[(int)state], conn);

        public static void SetImplementation(ConnectionStateEnum state, Type type)
        {
            if (!type.IsSubclassOf(typeof(MpConnectionState))) return;

            StateImpls[(int)state] = type;

            // The point of this attribute is to explicitly mark how to handle packet listeners from the base type.
            // If the base type is the lowest possible (MpConnectionState, which doesn't have any listeners), there is
            // no reason to warn when the annotation is missing.
            var typeAttr = type.GetAttribute<PacketHandlerClassAttribute>();
            if (typeAttr == null &&
                type.BaseType != typeof(MpConnectionState) &&
                type.BaseType != typeof(AsyncConnectionState))
                ServerLog.Log($"Packet handler {type.FullName} does not have a PacketHandlerClass attribute");

            var bindingFlags = BindingFlags.Instance | BindingFlags.Public;
            if (typeAttr?.inheritHandlers != true) bindingFlags |= BindingFlags.DeclaredOnly;

            foreach (var method in type.GetMethods(bindingFlags))
            {
                var attr = method.GetAttribute<PacketHandlerAttribute>();
                if (attr != null) RegisterPacketHandler(state, method, attr);

                var attr2 = method.GetAttribute<FragmentedPacketHandlerAttribute>();
                if (attr2 != null) RegisterFragmentedPacketHandler(state, method, attr2);

                var attr3 = method.GetAttribute<TypedPacketHandlerAttribute>();
                if (attr3 != null) RegisterTypedPacketHandler(state, method);
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

        private static void RegisterPacketHandler(ConnectionStateEnum state, Packets packet, bool allowFragmented,
            Func<PacketHandlerInvoker> produceInvoker)
        {
            var packetHandlerInfo = packetHandlers[(int)state, (int)packet];
            if (packetHandlerInfo == null)
            {
                packetHandlers[(int)state, (int)packet] =
                    new PacketHandlerInfo(produceInvoker(), allowFragmented);
                return;
            }
            if (packetHandlerInfo.Method != null)
                throw new Exception($"Packet {state}:{packet} already has a handler");

            if (!allowFragmented && packetHandlerInfo.FragmentHandler != null)
                throw new Exception($"Packet {state}:{packet} has a fragment handler despite not being allowed to");

            packetHandlers[(int)state, (int)packet] = packetHandlerInfo with
            {
                Method = produceInvoker(), Fragment = allowFragmented
            };
        }

        private static void RegisterPacketHandler(ConnectionStateEnum state, MethodInfo method, PacketHandlerAttribute attr)
        {
            if (method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(ByteReader))
                throw new Exception($"Bad packet handler signature for {method}: must have 1 parameter of type {typeof(ByteReader)}");

            RegisterPacketHandler(state, attr.packet, attr.allowFragmented, () =>
            {
                DynamicMethod invoker = new DynamicMethod($"PacketHandlerInvoker_{attr.packet}_{method.Name}",
                    typeof(void), [typeof(object), typeof(ByteReader)]);
                var il = invoker.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); // object target
                il.Emit(OpCodes.Castclass, method.DeclaringType ?? throw new InvalidOperationException());
                il.Emit(OpCodes.Ldarg_1); // ByteReader data
                il.Emit(OpCodes.Callvirt, method);
                il.Emit(OpCodes.Ret);
                return (PacketHandlerInvoker)invoker.CreateDelegate(typeof(PacketHandlerInvoker));
            });
        }

        private static void RegisterTypedPacketHandler(ConnectionStateEnum state, MethodInfo method)
        {
            if (method.GetParameters().Length != 1)
                throw new Exception($"Bad packet handler signature for {method}: must have exactly 1 parameter");

            var paramType = method.GetParameters()[0].ParameterType;
            if (!typeof(IPacket).IsAssignableFrom(paramType))
                throw new Exception($"Bad packet handler signature for {method}: the parameter must be of type IPacket");

            if (!paramType.IsStruct())
                throw new Exception($"Bad packet handler signature for {method}: the parameter must be a struct");

            var packetDef = paramType.GetAttribute<PacketDefinitionAttribute>();
            if (packetDef == null)
                throw new Exception($"Bad packet handler signature for {method}: the parameter's type must have a [PacketDefinition] attribute");

            RegisterPacketHandler(state, packetDef.packet, packetDef.allowFragmented,
                () =>
                {
                    var invoker = new DynamicMethod($"TypedPacketHandlerInvoker_{packetDef.packet}_{method.Name}",
                        typeof(void), [typeof(object), typeof(ByteReader)]);
                    var il = invoker.GetILGenerator();
                    var paramLocal = il.DeclareLocal(paramType);

                    il.Emit(OpCodes.Ldloca, paramLocal);
                    il.Emit(OpCodes.Initobj, paramType);

                    il.Emit(OpCodes.Ldloca, paramLocal);
                    il.Emit(OpCodes.Ldarg_1); // ByteReader data
                    il.Emit(OpCodes.Newobj, typeof(PacketReader).DeclaredConstructor([typeof(ByteReader)]));
                    // Use the type's method instead of just referencing the interface method to avoid additional
                    // indirection of going through the vtable.
                    il.Emit(OpCodes.Call,
                        paramType.GetMethod(nameof(IPacket.Bind), [typeof(PacketBuffer)]) ??
                        throw new InvalidOperationException());

                    il.Emit(OpCodes.Ldarg_0); // object target (handler's class instance)
                    il.Emit(OpCodes.Castclass, method.DeclaringType ?? throw new InvalidOperationException());
                    il.Emit(OpCodes.Ldloc, paramLocal);
                    il.Emit(OpCodes.Callvirt, method);

                    il.Emit(OpCodes.Ret);
                    return (PacketHandlerInvoker)invoker.CreateDelegate(typeof(PacketHandlerInvoker));
                });
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
