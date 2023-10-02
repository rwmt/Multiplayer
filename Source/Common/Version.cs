namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.9";
        public const int Protocol = 34;

        public const string ApiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
