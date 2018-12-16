namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.1.1";
        public const int Protocol = 2;

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
