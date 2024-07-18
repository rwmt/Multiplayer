namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.10.5";
        public const int Protocol = 47;

        public const string ApiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
