namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.9.1";
        public const int Protocol = 35;

        public const string ApiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
