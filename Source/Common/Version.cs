namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.1.9";
        public const int Protocol = 8;

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
