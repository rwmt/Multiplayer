using Multiplayer.Common;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{
    public interface ITickable
    {
        int TickableId { get; }

        Queue<ScheduledCommand> Cmds { get; }

        float TimeToTickThrough { get; set; }

        TimeSpeed DesiredTimeSpeed { get; }

        void SetDesiredTimeSpeed(TimeSpeed speed);

        float TickRateMultiplier(TimeSpeed speed);

        void Tick();

        void ExecuteCmd(ScheduledCommand cmd);
    }
}
