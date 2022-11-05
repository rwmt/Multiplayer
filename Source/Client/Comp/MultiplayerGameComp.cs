using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Comp
{
    public class MultiplayerGameComp : IExposable, IHasSemiPersistentData
    {
        public bool asyncTime;
        public bool debugMode;
        public bool logDesyncTraces;
        public PauseOnLetter pauseOnLetter;
        public TimeControl timeControl;
        public Dictionary<int, PlayerData> playerData = new(); // player id to player data

        public IdBlock globalIdBlock = new(int.MaxValue / 2, 1_000_000_000);

        public bool IsLowestWins => timeControl == TimeControl.LowestWins;

        public PlayerData LocalPlayerDataOrNull => playerData.GetValueOrDefault(Multiplayer.session.playerId);

        public MultiplayerGameComp(Game game)
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref asyncTime, "asyncTime", true, true);
            Scribe_Values.Look(ref debugMode, "debugMode");
            Scribe_Values.Look(ref logDesyncTraces, "logDesyncTraces");
            Scribe_Values.Look(ref pauseOnLetter, "pauseOnLetter");
            Scribe_Values.Look(ref timeControl, "timeControl");

            Scribe_Custom.LookIdBlock(ref globalIdBlock, "globalIdBlock");

            if (globalIdBlock == null)
            {
                // todo globalIdBlock was previously in WorldComp, this is a quick hack to make old saves compatible
                Log.Warning("Global id block was null, fixing...");
                globalIdBlock = new IdBlock(int.MaxValue / 2, 1_000_000_000);
            }
        }

        public void WriteSemiPersistent(ByteWriter writer)
        {
            SyncSerialization.WriteSync(writer, playerData);
        }

        public void ReadSemiPersistent(ByteReader reader)
        {
            playerData = SyncSerialization.ReadSync<Dictionary<int, PlayerData>>(reader);
            DebugSettings.godMode = LocalPlayerDataOrNull?.godMode ?? false;
        }

        [SyncMethod(debugOnly = true)]
        public void SetGodMode(int playerId, bool godMode)
        {
            playerData[playerId].godMode = godMode;
        }

        public TimeSpeed GetLowestTimeVote(int tickableId, bool excludePaused = false)
        {
            return (TimeSpeed)playerData.Values
                .SelectMany(p => p.AllTimeVotes.GetOrEmpty(tickableId))
                .Where(v => !excludePaused || v != TimeVote.Paused)
                .DefaultIfEmpty(TimeVote.Paused)
                .Min();
        }

        public void ResetAllTimeVotes(int tickableId)
        {
            playerData.Values.Do(p => p.SetTimeVote(tickableId, TimeVote.PlayerReset));
        }
    }

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
            if (vote is TimeVote.Reset or TimeVote.PlayerReset)
                timeVotes.Remove(tickableId);
            else if (vote is TimeVote.ResetAll or TimeVote.PlayerResetAll)
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
}
