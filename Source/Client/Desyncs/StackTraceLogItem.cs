using System.Reflection;

namespace Multiplayer.Client
{
    public class StackTraceLogItem
    {
        public MethodBase[] stackTrace;
        public string additionalInfo;
    }
}