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
        private static ProfilerDisposable instance = new();

        class ProfilerDisposable : IDisposable
        {
            public void Dispose()
            {
                DeepProfiler.End();
            }
        }

        public static IDisposable Section(string section)
        {
            DeepProfiler.Start(section);
            return instance;
        }
    }
}
