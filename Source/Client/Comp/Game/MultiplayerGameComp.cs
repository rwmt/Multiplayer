using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Comp
{
    public class MultiplayerGameComp : IExposable, IHasSessionData
    {
        public bool asyncTime;
        public bool multifaction;
        public bool debugMode;
        public bool logDesyncTraces;
        public PauseOnLetter pauseOnLetter;
        public TimeControl timeControl;
        public Dictionary<int, PlayerData> playerData = new(); // player id to player data
        public int nextSessionId;

        // Synced view table: player id -> viewed map uniqueID, with
        // VTRSync.WorldMapId for the planet view. Source of truth for the VTR
        // player counts - counts are derived from this table, never
        // incremented, so no send/wipe/disconnect ordering can drift them.
        // Written only from CommandType.PlayerCount commands (client view
        // announces and the server's disconnect removal) and scribed so join
        // points and rehosts keep the views instead of resetting every map to
        // the no-viewer rate.
        public Dictionary<int, int> playerViewedMaps = new();
        public int playerViewsVersion;

        public string idBlockBase64;

        public bool IsLowestWins => timeControl == TimeControl.LowestWins;

        public void SetPlayerViewedMap(int playerId, int mapId)
        {
            if (mapId == Patches.VTRSync.InvalidMapId)
                playerViewedMaps.Remove(playerId);
            else
                playerViewedMaps[playerId] = mapId;
            playerViewsVersion++;
        }

        public PlayerData LocalPlayerDataOrNull => playerData.GetValueOrDefault(Multiplayer.session.playerId);

        public void ExposeData()
        {
            Scribe_Values.Look(ref asyncTime, "asyncTime", true, true);
            Scribe_Values.Look(ref multifaction, "multifaction", false, true);
            Scribe_Values.Look(ref debugMode, "debugMode");
            Scribe_Values.Look(ref logDesyncTraces, "logDesyncTraces");
            Scribe_Values.Look(ref pauseOnLetter, "pauseOnLetter");
            Scribe_Values.Look(ref timeControl, "timeControl");
            Scribe_Values.Look(ref nextSessionId, "nextSessionId");

            Scribe_Collections.Look(ref playerViewedMaps, "playerViewedMaps", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Absent on saves predating the table; version bump invalidates
                // every comp's cached count after a (re)load
                playerViewedMaps ??= new Dictionary<int, int>();
                playerViewsVersion++;
            }

            // Store for back-compat conversion in GameExposeComponentsPatch
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref idBlockBase64, "globalIdBlock");
        }

        public void WriteSessionData(ByteWriter writer)
        {
            SyncSerialization.WriteSync(writer, playerData);
        }

        public void ReadSessionData(ByteReader reader)
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
