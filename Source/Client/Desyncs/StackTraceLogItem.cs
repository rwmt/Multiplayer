using System.Diagnostics;
using System.Reflection;

namespace Multiplayer.Client
{
    public class StackTraceLogItem
    {
        public StackTrace stackTrace;
        public int tick;
        public int hash;
        public string additionalInfo;

        public override string ToString()
        {
            return $"Tick:{tick} Hash:{hash} '{additionalInfo}'\n{stackTrace.ToString()}";
        }
    }
}
