namespace Multiplayer
{
    public static class MpLog
    {
        public static void Log(string s)
        {
            Verse.Log.Message(Multiplayer.username + " " + s);
        }
    }
}
