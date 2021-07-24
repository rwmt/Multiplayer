extern alias zip;
using Multiplayer.Common;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{
    public interface ITickable
    {
        float RealTimeToTickThrough { get; set; }

        TimeSpeed TimeSpeed { get; }

        Queue<ScheduledCommand> Cmds { get; }

        float TickRateMultiplier(TimeSpeed speed);

        void Tick();

        void ExecuteCmd(ScheduledCommand cmd);
    }
}
