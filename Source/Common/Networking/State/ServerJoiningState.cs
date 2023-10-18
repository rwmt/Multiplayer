using System.Threading.Tasks;

namespace Multiplayer.Common;

public class ServerJoiningState : AsyncConnectionState
{
    public ServerJoiningState(ConnectionBase connection) : base(connection)
    {
    }

    protected override async Task RunState()
    {
        HandleProtocol(await Packet(Packets.Client_Protocol));
        HandleUsername(await Packet(Packets.Client_Username));

        while (await Server.InitData() is null && await EndIfDead())
            if (Server.initDataState == InitDataState.Waiting)
                await RequestInitData();

        connection.Send(Packets.Server_UsernameOk);

        if (HandleClientJoinData(await Packet(Packets.Client_JoinData).Fragmented()) is false)
            return;

        if (Server.settings.pauseOnJoin)
            Server.commands.PauseAll();

        if (Server.settings.autoJoinPoint.HasFlag(AutoJoinPointFlags.Join))
            Server.worldData.TryStartJoinPointCreation();

        Server.playerManager.OnJoin(Player);
        Server.playerManager.SendInitDataCommand(Player);

        await Packet(Packets.Client_WorldRequest);

        connection.ChangeState(ConnectionStateEnum.ServerLoading);
    }

    private void HandleProtocol(ByteReader data)
    {
        int clientProtocol = data.ReadInt32();

        if (clientProtocol != MpVersion.Protocol)
            Player.Disconnect(MpDisconnectReason.Protocol, ByteWriter.GetBytes(MpVersion.Version, MpVersion.Protocol));
        else
            Player.SendPacket(Packets.Server_ProtocolOk, new object[] { Server.settings.hasPassword });
    }

    private void HandleUsername(ByteReader data)
    {
        if (Server.settings.hasPassword)
        {
            string password = data.ReadString();
            if (password != Server.settings.password)
            {
                Player.Disconnect(MpDisconnectReason.BadGamePassword);
                return;
            }
        }

        string username = data.ReadString();

        if (username.Length < MultiplayerServer.MinUsernameLength || username.Length > MultiplayerServer.MaxUsernameLength)
        {
            Player.Disconnect(MpDisconnectReason.UsernameLength);
            return;
        }

        if (!Player.IsArbiter && !MultiplayerServer.UsernamePattern.IsMatch(username))
        {
            Player.Disconnect(MpDisconnectReason.UsernameChars);
            return;
        }

        if (Server.GetPlayer(username) != null)
        {
            Player.Disconnect(MpDisconnectReason.UsernameAlreadyOnline);
            return;
        }

        connection.username = username;
    }

    private async Task RequestInitData()
    {
        Server.initDataState = InitDataState.Requested;
        Server.initDataSource = new TaskCompletionSource<ServerInitData?>();

        Player.SendPacket(Packets.Server_InitDataRequest, ByteWriter.GetBytes(Server.settings.syncConfigs));

        ServerLog.Verbose("Sent initial data request");

        var initData = await PacketOrNull(Packets.Client_InitData).Fragmented();

        if (initData != null)
        {
            Server.CompleteInitData(ServerInitData.Deserialize(initData));
        }
        else
        {
            Server.initDataState = InitDataState.Waiting;
            Server.initDataSource.SetResult(null);
        }
    }

    private bool HandleClientJoinData(ByteReader data)
    {
        var defTypeCount = data.ReadInt32();
        if (defTypeCount > 512)
        {
            Player.Disconnect("Too many defs");
            return false;
        }

        var defsResponse = new ByteWriter();
        var defsMatch = true;

        for (int i = 0; i < defTypeCount; i++)
        {
            var defType = data.ReadString(128);
            var defCount = data.ReadInt32();
            var defHash = data.ReadInt32();

            var status = DefCheckStatus.Ok;

            if (!Server.initData!.DefInfos.TryGetValue(defType, out DefInfo info))
                status = DefCheckStatus.Not_Found;
            else if (info.count != defCount)
                status = DefCheckStatus.Count_Diff;
            else if (info.hash != defHash)
                status = DefCheckStatus.Hash_Diff;

            if (status != DefCheckStatus.Ok)
                defsMatch = false;

            defsResponse.WriteByte((byte)status);
        }

        connection.SendFragmented(
            Packets.Server_JoinData,
            Server.settings.gameName,
            Player.id,
            Server.initData!.RwVersion,
            MpVersion.Version,
            defsResponse.ToArray(),
            Server.initData.RawData
        );

        return defsMatch;
    }
}

public enum DefCheckStatus : byte
{
    Ok,
    Not_Found,
    Count_Diff,
    Hash_Diff,
}
