using Multiplayer.Common;

namespace Tests;

public class TestJoiningState : AsyncConnectionState
{
    public TestJoiningState(ConnectionBase connection) : base(connection)
    {
    }

    private const string RwVersion = "1.0.0";

    protected override async Task RunState()
    {
        connection.Send(Packets.Client_Protocol, MpVersion.Protocol);
        await Packet(Packets.Server_ProtocolOk);

        connection.Send(Packets.Client_Username, connection.username!);
        await Packet(Packets.Server_InitDataRequest);

        connection.Send(
            Packets.Client_InitData,
            Array.Empty<byte>(),
            RwVersion,
            Array.Empty<int>(),
            Array.Empty<int>(),
            RoundModeEnum.ToNearest, RoundModeEnum.ToNearest,
            Array.Empty<object>()
        );

        await Packet(Packets.Server_UsernameOk);

        connection.Send(
            Packets.Client_JoinData,
            RoundModeEnum.ToNearest, RoundModeEnum.ToNearest, 0
        );

        await Packet(Packets.Server_JoinData).Fragmented();

        connection.Send(Packets.Client_WorldRequest);
        await Packet(Packets.Server_WorldDataStart);
        await Packet(Packets.Server_WorldData).Fragmented();

        connection.Close(MpDisconnectReason.Generic);
        connection.ChangeState(ConnectionStateEnum.Disconnected);
    }
}
