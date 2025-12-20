using System;
using System.Collections.Generic;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Steamworks;

namespace Multiplayer.Client.Util;

/// Contains all the data required to create a connection for the transport type (i.e., IP and port for LiteNetLib, and
/// the host's steam id for Steam)
public interface IConnector
{
    /// Does include the connector's prefix.
    public string GetConnectionString();
    public (ConnectionBase, BaseConnectingWindow) Connect();
}

/// Utility class for creating connectors and parsing them from a string based on their unique prefix.
public static class ConnectorRegistry
{
    private delegate bool ConnStringParser(string connString, out IConnector connector);

    private static readonly List<(string prefix, ConnStringParser parser)> ConnStringParsers =
    [
        (SteamConnector.Prefix, SteamConnector.TryParse),
        (LiteNetConnector.Prefix, LiteNetConnector.TryParse)
    ];

    public static bool TryParse(string connString, out IConnector connector)
    {
        connector = null;
        foreach (var (prefix, parser) in ConnStringParsers)
        {
            var prefixColon = $"{prefix}:";
            if (connString.StartsWith(prefixColon)) return parser(connString.RemovePrefix(prefixColon), out connector);
        }

        return false;
    }

    public static IConnector Steam(CSteamID remoteId) => new SteamConnector(remoteId);
    public static IConnector LiteNet(string addr, int port) => new LiteNetConnector(addr, port);
}

public class SteamConnector(CSteamID remoteId) : IConnector
{
    public static string Prefix => "steam";

    /// Does not check if Prefix matches. Use ConnectorRegistry instead if that's desired.
    public static bool TryParse(string connString, out IConnector connector)
    {
        if (ulong.TryParse(connString, out ulong steamUser))
        {
            connector = new SteamConnector((CSteamID)steamUser);
            return true;
        }

        connector = null;
        return false;
    }

    public string GetConnectionString() => $"{Prefix}:{remoteId}";

    public (ConnectionBase, BaseConnectingWindow) Connect() =>
        (new SteamClientConn(remoteId), new SteamConnectingWindow(remoteId));
}

public class LiteNetConnector(string address, int port) : IConnector
{
    public static string Prefix => "lnl";

    /// Does not check if Prefix matches. Use ConnectorRegistry instead if that's desired.
    public static bool TryParse(string connString, out IConnector connector)
    {
        var split = connString.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length == 0)
        {
            connector = new LiteNetConnector("127.0.0.1", MultiplayerServer.DefaultPort);
            return true;
        }

        if (split.Length == 1)
        {
            connector = new LiteNetConnector(split[0], MultiplayerServer.DefaultPort);
            return true;
        }

        if (split.Length == 2 && int.TryParse(split[1], out int port))
        {
            connector = new LiteNetConnector(split[0], port);
            return true;
        }

        connector = null;
        return false;
    }

    public string GetConnectionString() => $"{Prefix}:{address}:{port}";

    public (ConnectionBase, BaseConnectingWindow) Connect() =>
        (ClientLiteNetConnection.Connect(address, port), new ConnectingWindow(address, port));
}
