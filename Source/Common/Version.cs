namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.3";
        public const int Protocol = 10;

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
