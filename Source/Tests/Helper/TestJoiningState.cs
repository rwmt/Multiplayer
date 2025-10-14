using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

public class TestJoiningState : AsyncConnectionState
{
    public TestJoiningState(ConnectionBase connection) : base(connection)
    {
    }

    private const string RwVersion = "1.0.0";

    protected override async Task RunState()
    {
        connection.Send(ClientProtocolPacket.Current());
        await TypedPacket<ServerProtocolOkPacket>();

        connection.Send(Packets.Client_Username, connection.username!);
        var p = await Packet(Packets.Server_InitDataRequest);
        p.Seek(p.Length); // Pretend to read to avoid an error about not fully reading a packet

        connection.Send(
            Packets.Client_InitData,
            Array.Empty<byte>(),
            RwVersion,
            Array.Empty<int>(),
            Array.Empty<int>(),
            RoundModeEnum.ToNearest, RoundModeEnum.ToNearest,
            Array.Empty<object>()
        );

        p = await Packet(Packets.Server_UsernameOk);
        p.Seek(p.Length);

        connection.Send(
            Packets.Client_JoinData,
            RoundModeEnum.ToNearest, RoundModeEnum.ToNearest, 0
        );

        p = await Packet(Packets.Server_JoinData).Fragmented();
        p.Seek(p.Length);

        connection.Send(Packets.Client_WorldRequest);
        p = await Packet(Packets.Server_WorldDataStart);
        p.Seek(p.Length);
        p = await Packet(Packets.Server_WorldData).Fragmented();
        p.Seek(p.Length);

        connection.Close(MpDisconnectReason.Generic);
        connection.ChangeState(ConnectionStateEnum.Disconnected);
    }
}
