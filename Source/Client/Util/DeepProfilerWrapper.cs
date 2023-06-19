using System;
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
