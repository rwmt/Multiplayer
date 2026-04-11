using System.Linq;
using System.Reflection;

namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string SimpleVersion = "0.11.4";
        public const int Protocol = 54;

        public static readonly string? GitHash = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == "GitHash")?.Value;

        public static readonly string Version = SimpleVersion + (GitHash != null ? $"+{GitHash}" : "");

        public static readonly string? GitDescription = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == "GitDescription")?.Value;

        public const string ApiAssemblyName = "0MultiplayerAPI";

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
