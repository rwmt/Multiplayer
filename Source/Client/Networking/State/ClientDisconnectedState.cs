using Multiplayer.Common;

namespace Multiplayer.Client;

/// <summary>
/// Stato client per connessione disconnessa. Non fa nulla, serve solo come placeholder.
/// </summary>
public class ClientDisconnectedState(ConnectionBase connection) : ClientBaseState(connection)
{
}