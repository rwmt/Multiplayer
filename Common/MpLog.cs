using System;

namespace Multiplayer.Common
{
    public static class MpLog
    {
        public static Action<string> action;

        public static void Log(string s, params object[] args)
        {
            s = String.Format(s, args);

            if (action != null)
                action(s);
            else
                Console.WriteLine(s);
        }

        public static void LogLines(params string[] arr)
        {
            string s = String.Join("\n", arr);
            if (action != null)
                action(s);
            else
                Console.WriteLine(s);
        }
    }
}
