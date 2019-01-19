namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.4.3";
        public const int Protocol = 12;

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
