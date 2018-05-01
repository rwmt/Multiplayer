using System;

namespace Multiplayer.Common
{
    public static class MpLog
    {
        public static Action<string> info;

        public static void Log(string s, params object[] args)
        {
            s = String.Format(s, args);

            if (info != null)
                info(s);
            else
                Console.WriteLine(s);
        }

        public static void LogLines(params string[] arr)
        {
            string s = String.Join("\n", arr);
            if (info != null)
                info(s);
            else
                Console.WriteLine(s);
        }
    }
}
