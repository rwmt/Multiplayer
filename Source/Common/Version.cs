namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.5";
        public const int Protocol = 18;

        public const string apiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
