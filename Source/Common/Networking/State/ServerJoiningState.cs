using System;
using System.IO;
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

        if (!HandleClientJoinData(await TypedPacket<ClientJoinDataPacket>()))
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
        {
            Player.SendPacket(new ServerProtocolOkPacket(Server.settings.hasPassword));

            if (Server.BootstrapMode)
            {
                var settingsMissing = !File.Exists(Path.Combine(AppContext.BaseDirectory, "settings.toml"));
                var saveMissing = !File.Exists(Path.Combine(AppContext.BaseDirectory, "save.zip"));
                connection.Send(new ServerBootstrapPacket(true, settingsMissing, saveMissing));
            }
        }
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
        ClientInitDataPacket? initData = null;
        var completionSource = Server.StartInitData();
        try
        {
            Player.SendPacket(new ServerInitDataRequestPacket(Server.settings.syncConfigs));

            ServerLog.Verbose("Sent initial data request");
            initData = await TypedPacketOrNull<ClientInitDataPacket>();
        }
        finally
        {
            // Invoking StartInitData and abandoning the completion source in case of an exception would mean a server
            // restart is necessary to try and set init data again. Make sure the server is more graceful in that case.
            completionSource.SetResult(initData != null
                // cast to non-nullable, for whatever reason C# isn't able to infer that from the != null check
                ? ServerInitData.FromNet((ClientInitDataPacket)initData)
                : null);
        }
    }

    private bool HandleClientJoinData(ClientJoinDataPacket packet)
    {
        var serverInitData = Server.InitData ??
                             throw new Exception("Server init data is null during handling of client join data");
        if ((packet.modCtorRoundMode, packet.staticCtorRoundMode) != serverInitData.RoundModes)
        {
            Player.Disconnect($"FP round modes don't match: {(packet.modCtorRoundMode, packet.staticCtorRoundMode)} != {serverInitData.RoundModes}");
            return false;
        }

        var defStatus = new DefCheckStatus[packet.defInfos.Length];
        var defsMatch = true;

        for (var i = 0; i < packet.defInfos.Length; i++)
        {
            var status = DefCheckStatus.Ok;

            var defInfo = packet.defInfos[i];
            if (!serverInitData.DefInfos.TryGetValue(defInfo.name, out DefInfo info))
                status = DefCheckStatus.Not_Found;
            else if (info.count != defInfo.count)
                status = DefCheckStatus.Count_Diff;
            else if (info.hash != defInfo.hash)
                status = DefCheckStatus.Hash_Diff;

            if (status != DefCheckStatus.Ok)
                defsMatch = false;

            defStatus[i] = status;
        }

        connection.SendFragmented(new ServerJoinDataPacket
        {
            gameName = Server.settings.gameName,
            playerId = Player.id,
            rwVersion = serverInitData.RwVersion,
            mpVersion = MpVersion.Version,
            defStatus = defStatus,
            rawServerInitData = serverInitData.RawData
        }.Serialize());

        if (Server.BootstrapMode)
        {
            connection.ChangeState(ConnectionStateEnum.ServerBootstrap);
            return defsMatch;
        }

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
