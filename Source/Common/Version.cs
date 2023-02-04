namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.7.3";
        public const int Protocol = 29;

        public const string ApiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
