namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.4.7.1";
        public const int Protocol = 16;

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
