using System;
using System.Threading.Tasks;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

public class ServerJoiningState : AsyncConnectionState
{
    public ServerJoiningState(ConnectionBase connection) : base(connection)
    {
    }

    protected override async Task RunState()
    {
        HandleProtocol(await TypedPacket<ClientProtocolPacket>());
        HandleUsername(await TypedPacket<ClientUsernamePacket>());

        while (await Server.InitDataTask() is null && await EndIfDead())
            if (Server.InitDataState == InitDataState.Waiting)
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

    private void HandleProtocol(ClientProtocolPacket packet)
    {
        if (packet.protocolVersion != MpVersion.Protocol)
            Player.Disconnect(MpDisconnectReason.Protocol, ByteWriter.GetBytes(MpVersion.Version, MpVersion.Protocol));
        else
            Player.conn.Send(new ServerProtocolOkPacket(Server.settings.hasPassword));
    }

    private void HandleUsername(ClientUsernamePacket packet)
    {
        if (Server.settings.hasPassword)
        {
            string? password = packet.password;
            if (password != Server.settings.password)
            {
                Player.Disconnect(MpDisconnectReason.BadGamePassword);
                return;
            }
        }

        string username = packet.username;

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
        ByteReader? initData = null;
        var completionSource = Server.StartInitData();
        try
        {
            Player.SendPacket(Packets.Server_InitDataRequest, ByteWriter.GetBytes(Server.settings.syncConfigs));

            ServerLog.Verbose("Sent initial data request");
            initData = await PacketOrNull(Packets.Client_InitData).Fragmented();
        }
        finally
        {
            // Invoking StartInitData and abandoning the completion source in case of an exception would mean a server
            // restart is necessary to try and set init data again. Make sure the server is more graceful in that case.
            completionSource.SetResult(initData != null ? ServerInitData.Deserialize(initData) : null);
        }
    }

    private bool HandleClientJoinData(ByteReader data)
    {
        var modCtorRoundMode = data.ReadEnum<RoundModeEnum>();
        var staticCtorRoundMode = data.ReadEnum<RoundModeEnum>();

        var serverInitData = Server.InitData ??
                             throw new Exception("Server init data is null during handling of client join data");
        if ((modCtorRoundMode, staticCtorRoundMode) != serverInitData.RoundModes)
        {
            Player.Disconnect($"FP round modes don't match: {(modCtorRoundMode, staticCtorRoundMode)} != {serverInitData.RoundModes}");
            return false;
        }

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

            if (!serverInitData.DefInfos.TryGetValue(defType, out DefInfo info))
                status = DefCheckStatus.Not_Found;
            else if (info.count != defCount)
                status = DefCheckStatus.Count_Diff;
            else if (info.hash != defHash)
                status = DefCheckStatus.Hash_Diff;

            if (status != DefCheckStatus.Ok)
                defsMatch = false;

            defsResponse.WriteEnum(status);
        }

        connection.SendFragmented(
            Packets.Server_JoinData,
            Server.settings.gameName,
            Player.id,
            serverInitData.RwVersion,
            MpVersion.Version,
            defsResponse.ToArray(),
            serverInitData.RawData
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
