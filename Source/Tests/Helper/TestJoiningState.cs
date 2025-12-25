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

        // Newer protocol: server can additionally inform us during handshake that it's in bootstrap mode.
        // Consume it to keep the test handshake robust across versions.
        await TypedPacket<ServerBootstrapPacket>();

        connection.Send(new ClientUsernamePacket(connection.username!));
        await TypedPacket<ServerInitDataRequestPacket>();

        connection.Send(new ClientInitDataPacket
        {
            rwVersion = RwVersion,
            debugOnlySyncCmds = [],
            hostOnlySyncCmds = [],
            modCtorRoundMode = RoundModeEnum.ToNearest,
            staticCtorRoundMode = RoundModeEnum.ToNearest,
            defInfos = [],
            rawData = []
        });

        var p = await Packet(Packets.Server_UsernameOk);
        p.Seek(p.Length); // Pretend to read to avoid an error about not fully reading a packet

        connection.Send(new ClientJoinDataPacket
        {
            modCtorRoundMode = RoundModeEnum.ToNearest, staticCtorRoundMode = RoundModeEnum.ToNearest, defInfos = []
        });

        await TypedPacket<ServerJoinDataPacket>();

        connection.Send(Packets.Client_WorldRequest);
        p = await Packet(Packets.Server_WorldDataStart);
        p.Seek(p.Length);
        p = await Packet(Packets.Server_WorldData).Fragmented();
        p.Seek(p.Length);

        connection.Close(MpDisconnectReason.Generic);
        connection.ChangeState(ConnectionStateEnum.Disconnected);
    }
}
