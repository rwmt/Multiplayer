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
        class ProfilerDisposable : IDisposable
        {
            public ProfilerDisposable(string section)
            {
                DeepProfiler.Start(section);
            }

            public void Dispose()
            {
                DeepProfiler.End();
            }
        }

        public static IDisposable Section(string section)
        {
            return new ProfilerDisposable(section);
        }
    }
}
