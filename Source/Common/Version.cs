namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.4.8";
        public const int Protocol = 17;

        public const string apiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
