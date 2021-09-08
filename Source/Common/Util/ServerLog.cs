using System;
using LiteNetLib;

namespace Multiplayer.Common
{
    public class ServerLog : INetLogger
    {
        public static Action<string> info;
        public static Action<string> error;

        public static void Log(string s)
        {
            if (info != null)
                info(s);
            else
                Console.WriteLine(s);
        }

        public static void Error(string s)
        {
            if (error != null)
                error(s);
            else
                Console.Error.WriteLine(s);
        }

        public void WriteNet(NetLogLevel level, string str, params object[] args)
        {
            if (level == NetLogLevel.Error)
                Error(string.Format(str, args));
            else
                Log(string.Format(str, args));
        }
    }
}
