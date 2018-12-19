namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.1.7";
        public const int Protocol = 6;

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
