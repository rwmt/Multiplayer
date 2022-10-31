extern alias zip;
using Multiplayer.Common;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{
    public interface ITickable
    {
        int TickableId { get; }

        float RealTimeToTickThrough { get; set; }

        TimeSpeed TimeSpeed { get; set; }

        Queue<ScheduledCommand> Cmds { get; }

        float TickRateMultiplier(TimeSpeed speed);

        void Tick();

        void ExecuteCmd(ScheduledCommand cmd);
    }
}
