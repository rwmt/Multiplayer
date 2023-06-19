using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Multiplayer.Common;

namespace Tests;

public class TestNetListener : INetEventListener
{
    public ConnectionBase? conn;

    public void OnPeerConnected(NetPeer peer)
    {
        conn = new LiteNetConnection(peer);
        conn.username = "test1";
        conn.ChangeState(ConnectionStateEnum.ClientJoining);

        Console.WriteLine("TestNetListener: OnPeerConnected");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Console.WriteLine($"Peer disconnected {disconnectInfo.Reason}");
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Assert.Fail($"Network error: {endPoint} {socketError}");
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
    {
        try
        {
            byte[] data = reader.GetRemainingBytes();
            conn!.HandleReceiveRaw(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        } catch (Exception e)
        {
            ServerLog.Error($"Exception in OnNetworkReceive: {e}");
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
    }
}
