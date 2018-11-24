using System;

namespace Multiplayer.Common
{
    public static class MpLog
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
    }
}
