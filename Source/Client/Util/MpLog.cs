using System.Diagnostics;

namespace Multiplayer.Client.Util
{
    public static class MpLog
    {
        public static void Log(string msg)
        {
            Verse.Log.Message($"{Multiplayer.username} {TickPatch.Timer} {msg}");
        }

        public static void Warn(string msg)
        {
            Verse.Log.Warning($"{Multiplayer.username} {TickPatch.Timer} {msg}");
        }

        public static void Error(string msg)
        {
            Verse.Log.Error($"{Multiplayer.username} {TickPatch.Timer} {msg}");
        }

        [Conditional("DEBUG")]
        public static void Debug(string msg)
        {
            Verse.Log.Message($"{Multiplayer.username} {TickPatch.Timer} {msg}");
        }
    }
}
