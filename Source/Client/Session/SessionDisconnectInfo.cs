using System;
using LiteNetLib;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client;

public struct SessionDisconnectInfo
{
    public string titleTranslated;
    public string descTranslated;
    public string specialButtonTranslated;
    public Action specialButtonAction;
    public bool wideWindow;

    public static SessionDisconnectInfo From(MpDisconnectReason reason, ByteReader reader)
    {
        var disconnectInfo = new SessionDisconnectInfo();
        string titleKey = null;
        string descKey = null;

        if (reason == MpDisconnectReason.GenericKeyed) titleKey = reader.ReadString();

        if (reason == MpDisconnectReason.Protocol)
        {
            titleKey = "MpWrongProtocol";

            string strVersion = reader.ReadString();
            int proto = reader.ReadInt32();

            disconnectInfo.wideWindow = true;
            disconnectInfo.descTranslated =
                "MpWrongMultiplayerVersionDesc".Translate(strVersion, proto, MpVersion.Version, MpVersion.Protocol);

            if (proto < MpVersion.Protocol)
                disconnectInfo.descTranslated += "\n" + "MpWrongVersionUpdateInfoHost".Translate();
            else
                disconnectInfo.descTranslated += "\n" + "MpWrongVersionUpdateInfo".Translate();
        }

        if (reason == MpDisconnectReason.ConnectingFailed)
        {
            var netReason = reader.ReadEnum<DisconnectReason>();

            disconnectInfo.titleTranslated =
                netReason == DisconnectReason.ConnectionFailed
                    ? "MpConnectionFailed".Translate()
                    : "MpConnectionFailedWithInfo".Translate(netReason.ToString().CamelSpace().ToLowerInvariant());
        }

        if (reason == MpDisconnectReason.NetFailed)
        {
            var netReason = reader.ReadEnum<DisconnectReason>();

            disconnectInfo.titleTranslated =
                "MpDisconnectedWithInfo".Translate(netReason.ToString().CamelSpace().ToLowerInvariant());
        }

        if (reason == MpDisconnectReason.UsernameAlreadyOnline)
        {
            titleKey = "MpInvalidUsernameAlreadyPlaying";
            descKey = "MpChangeUsernameInfo";

            var newName = Multiplayer.username.Substring(0,
                Math.Min(Multiplayer.username.Length, MultiplayerServer.MaxUsernameLength - 3));
            newName += new Random().Next(1000);

            disconnectInfo.specialButtonTranslated = "MpConnectAsUsername".Translate(newName);
            // Once disconnected, Multiplayer.session is set to null, so we need to keep our own copy to be able to
            // reconnect.
            var session = Multiplayer.session;
            disconnectInfo.specialButtonAction = () => session.Reconnect(newName);
        }

        if (reason == MpDisconnectReason.UsernameLength)
        {
            titleKey = "MpInvalidUsernameLength";
            descKey = "MpChangeUsernameInfo";
        }

        if (reason == MpDisconnectReason.UsernameChars)
        {
            titleKey = "MpInvalidUsernameChars";
            descKey = "MpChangeUsernameInfo";
        }

        if (reason == MpDisconnectReason.ServerClosed) titleKey = "MpServerClosed";
        if (reason == MpDisconnectReason.ServerFull) titleKey = "MpServerFull";
        if (reason == MpDisconnectReason.ServerStarting) titleKey = "MpDisconnectServerStarting";
        if (reason == MpDisconnectReason.Kick) titleKey = "MpKicked";
        if (reason == MpDisconnectReason.ServerPacketRead) descKey = "MpPacketErrorRemote";
        if (reason == MpDisconnectReason.BadGamePassword) descKey = "MpBadGamePassword";

        disconnectInfo.titleTranslated ??= titleKey?.Translate();
        disconnectInfo.descTranslated ??= descKey?.Translate();

        Log.Message($"Processed disconnect packet. Title: {disconnectInfo.titleTranslated} ({titleKey}), " +
                    $"description: {disconnectInfo.descTranslated} ({descKey})");

        return disconnectInfo;
    }
}
