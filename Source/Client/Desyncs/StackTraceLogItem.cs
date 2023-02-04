using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Multiplayer.Client.Desyncs;
using Verse;

namespace Multiplayer.Client
{
    public abstract class StackTraceLogItem
    {
        public int tick;
        public int hash;

        public abstract string AdditionalInfo { get; }

        public abstract string StackTraceString { get; }

        public override string ToString()
        {
            return $"Tick:{tick} Hash:{hash} '{AdditionalInfo}'\n{StackTraceString}";
        }

        public virtual void ReturnToPool() { }
    }

    public class StackTraceLogItemObj : StackTraceLogItem
    {
        public StackTrace stackTrace;
        public string additionalInfo;

        public override string AdditionalInfo => additionalInfo;

        public override string StackTraceString { get => stackTrace.ToString(); }
    }

    public class StackTraceLogItemRaw : StackTraceLogItem
    {
        public long[] raw = new long[DeferredStackTracingImpl.MaxDepth];
        public int depth;

        public int iters;
        public ThingDef thingDef;
        public int thingId;
        public string factionName;
        public string moreInfo;

        public override string AdditionalInfo => $"{thingDef}{thingId} {factionName} {depth} {iters} {moreInfo}";

        static Dictionary<long, string> methodNames = new Dictionary<long, string>();

        public override string StackTraceString
        {
            get
            {
                var builder = new StringBuilder();
                for (int i = 0; i < depth; i++)
                {
                    var addr = raw[i];

                    if (!methodNames.TryGetValue(addr, out string method))
                        methodNames[addr] = method = Native.MethodNameFromAddr(raw[i], false);

                    if (method != null)
                        builder.AppendLine(SyncCoordinator.MethodNameWithIL(method));
                    else
                        builder.AppendLine("Null");
                }

                return builder.ToString();
            }
        }

        public override void ReturnToPool()
        {
            depth = 0;
            iters = 0;
            thingId = 0;
            thingDef = null;
            factionName = null;
            moreInfo = null;

            SimplePool<StackTraceLogItemRaw>.Return(this);
        }
    }
}
