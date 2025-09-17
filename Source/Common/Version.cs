using System.Linq;
using System.Reflection;

namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.11.0";
        public const int Protocol = 49;

        public static readonly string? GitHash = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == "GitHash")?.Value;

        public static readonly string VersionWithHash = Version + (GitHash != null ? $"+{GitHash}" : "");

        public const string ApiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
