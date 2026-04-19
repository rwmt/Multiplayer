using Multiplayer.Common;

namespace Tests;

public static class Utils
{
    public static T? GetState<T>(this ConnectionBase connection) where T : MpConnectionState => (T?)connection.StateObj;
}
