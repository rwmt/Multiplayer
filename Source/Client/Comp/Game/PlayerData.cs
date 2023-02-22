using System.Collections.Generic;
using Multiplayer.API;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Comp;

public class PlayerData : ISynchronizable
{
    public bool canUseDevMode;
    public bool godMode;
    private Dictionary<int, TimeVote> timeVotes = new();

    public Dictionary<int, TimeVote> AllTimeVotes => timeVotes;

    public void Sync(SyncWorker sync)
    {
        sync.Bind(ref canUseDevMode);
        sync.Bind(ref godMode);
        sync.Bind(ref timeVotes);
    }

    public void SetContext()
    {
        Prefs.data.devMode = canUseDevMode;
        DebugSettings.godMode = godMode;
    }

    public void SetTimeVote(int tickableId, TimeVote vote)
    {
        if (vote is TimeVote.ResetTickable or TimeVote.PlayerResetTickable)
            timeVotes.Remove(tickableId);
        else if (vote is TimeVote.ResetGlobal or TimeVote.PlayerResetGlobal)
            timeVotes.Clear();
        else
            timeVotes[tickableId] = vote;
    }

    public TimeVote? GetTimeVoteOrNull(int tickableId)
    {
        if (timeVotes.TryGetValue(tickableId, out var vote))
            return vote;
        return null;
    }
}
