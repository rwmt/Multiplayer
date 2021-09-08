using Ionic.Zlib;
using System;
using System.IO;
using RestSharp;
using Verse;

namespace Multiplayer.Common
{
    // Unused
    public class ServerSteamState : MpConnectionState
    {
        public ServerSteamState(ConnectionBase conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_SteamRequest)]
        public void HandleSteamRequest(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ServerJoining;
            connection.Send(Packets.Server_SteamAccept);
        }
    }
}
