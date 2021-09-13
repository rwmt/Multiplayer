using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client.Util
{
    public static class DeepProfilerWrapper
    {
        public class ProfilerDisposable : IDisposable
        {
            public void Dispose()
            {
                DeepProfiler.End();
            }
        }

        public static ProfilerDisposable Section(string section)
        {
            DeepProfiler.Start(section);
            return new ProfilerDisposable();
        }
    }
}
