using System;

namespace Multiplayer.Common
{
    public static class MpLog
    {
        public static Action<string> action;

        public static void Log(string s)
        {
            if (action != null)
                action(s);
            else
                Console.WriteLine(s);
        }
    }
}
