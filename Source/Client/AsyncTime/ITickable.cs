using System.Collections.Generic;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client
{
    public interface ITickable
    {
        int TickableId { get; }

        Queue<ScheduledCommand> Cmds { get; }

        float TimeToTickThrough { get; set; }

        TimeSpeed DesiredTimeSpeed { get; set; }

        float TickRateMultiplier(TimeSpeed speed);

        void Tick();

        void ExecuteCmd(ScheduledCommand cmd);
    }
}
