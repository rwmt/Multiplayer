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
        public bool multifaction;
        public bool debugMode;
        public bool logDesyncTraces;
        public PauseOnLetter pauseOnLetter;
        public TimeControl timeControl;
        public Dictionary<int, PlayerData> playerData = new(); // player id to player data

        public string idBlockBase64;

        public bool IsLowestWins => timeControl == TimeControl.LowestWins;

        public PlayerData LocalPlayerDataOrNull => playerData.GetValueOrDefault(Multiplayer.session.playerId);

        public void ExposeData()
        {
            Scribe_Values.Look(ref asyncTime, "asyncTime", true, true);
            Scribe_Values.Look(ref multifaction, "multifaction", false, true);
            Scribe_Values.Look(ref debugMode, "debugMode");
            Scribe_Values.Look(ref logDesyncTraces, "logDesyncTraces");
            Scribe_Values.Look(ref pauseOnLetter, "pauseOnLetter");
            Scribe_Values.Look(ref timeControl, "timeControl");

            // Store for back-compat conversion in GameExposeComponentsPatch
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref idBlockBase64, "globalIdBlock");
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
            playerData.Values.Do(p => p.SetTimeVote(tickableId, TimeVote.PlayerResetTickable));
        }
    }
}
