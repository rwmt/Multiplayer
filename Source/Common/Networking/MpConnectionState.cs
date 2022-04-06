using System;
using System.Reflection;
using HarmonyLib;

namespace Multiplayer.Common
{
    public abstract class MpConnectionState
    {
        public readonly ConnectionBase connection;

        protected ServerPlayer Player => connection.serverPlayer;
        protected MultiplayerServer Server => MultiplayerServer.instance;

        public MpConnectionState(ConnectionBase connection)
        {
            this.connection = connection;
        }

        public virtual void StartState()
        {
        }

        public static Type[] connectionImpls = new Type[(int)ConnectionStateEnum.Count];
        public static PacketHandlerInfo[,] packetHandlers = new PacketHandlerInfo[(int)ConnectionStateEnum.Count, (int)Packets.Count];

        public static void SetImplementation(ConnectionStateEnum state, Type type)
        {
            if (!type.IsSubclassOf(typeof(MpConnectionState))) return;

            connectionImpls[(int)state] = type;

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var attr = method.GetAttribute<PacketHandlerAttribute>();
                if (attr == null)
                    continue;

                if (method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(ByteReader))
                    continue;

                bool fragment = method.GetAttribute<IsFragmentedAttribute>() != null;
                packetHandlers[(int)state, (int)attr.packet] = new PacketHandlerInfo(MethodInvoker.GetHandler(method), fragment);
            }
        }
    }

}
