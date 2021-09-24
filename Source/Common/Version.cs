namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.6.0.1";
        public const int Protocol = 24;

        public const string ApiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
