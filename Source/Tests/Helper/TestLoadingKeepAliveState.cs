using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

public class TestLoadingKeepAliveState : AsyncConnectionState
{
    public TestLoadingKeepAliveState(ConnectionBase connection) : base(connection)
    {
    }

    [TypedPacketHandler]
    public void HandleKeepAlive(ServerKeepAlivePacket packet) { }

    private const string RwVersion = "1.0.0";

    protected override async Task RunState()
    {
        connection.Send(ClientProtocolPacket.Current());
        await TypedPacket<ServerProtocolOkPacket>();

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

        var packet = await Packet(Packets.Server_UsernameOk);
        packet.Seek(packet.Length);

        connection.Send(new ClientJoinDataPacket
        {
            modCtorRoundMode = RoundModeEnum.ToNearest,
            staticCtorRoundMode = RoundModeEnum.ToNearest,
            defInfos = []
        });

        await TypedPacket<ServerJoinDataPacket>();

        connection.Send(Packets.Client_WorldRequest);
        connection.Send(new ClientKeepAlivePacket(0, 0, false, 0), false);

        packet = await Packet(Packets.Server_WorldDataStart);
        packet.Seek(packet.Length);
        packet = await Packet(Packets.Server_WorldData).Fragmented();
        packet.Seek(packet.Length);

        connection.Close(MpDisconnectReason.Generic);
        connection.ChangeState(ConnectionStateEnum.Disconnected);
    }
}
