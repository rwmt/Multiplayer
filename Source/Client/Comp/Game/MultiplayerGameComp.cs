using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using UnityEngine;
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

        public string idBlockBase64;

        // Bucketed by placer faction loadID; SortedDictionary guarantees identical enumeration on every client.
        public SortedDictionary<int, List<PingInfo>> markersByFaction = new();
        public int nextMarkerId;

        // Bumped on every markersByFaction mutation; read by PingMenuWindow's row cache. Runtime-only.
        public int markersVersion;

        // Host-authoritative, copied from ServerSettings at game start - every client must agree (drives FIFO eviction).
        public int markerCapPerPlayer = PingMarkerCap.Default;

        // Materialized merge of markersByFaction.Values; rebuilt on markersVersion change.
        private readonly List<PingInfo> cachedAllMarkers = new();
        private int cachedAllMarkersVersion = -1;

        public IReadOnlyList<PingInfo> AllMarkers
        {
            get
            {
                if (cachedAllMarkersVersion != markersVersion)
                {
                    cachedAllMarkers.Clear();
                    foreach (var bucket in markersByFaction.Values)
                        cachedAllMarkers.AddRange(bucket);
                    cachedAllMarkersVersion = markersVersion;
                }
                return cachedAllMarkers;
            }
        }

        public List<PingInfo> GetOrCreateFactionMarkers(int factionLoadId)
        {
            if (!markersByFaction.TryGetValue(factionLoadId, out var bucket))
            {
                bucket = new List<PingInfo>();
                markersByFaction[factionLoadId] = bucket;
            }
            return bucket;
        }

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
            Scribe_Values.Look(ref nextSessionId, "nextSessionId");

            // Re-bucket must run in LoadingVars - Scribe_Collections.Look only populates the ref then.
            List<PingInfo> markersFlat = Scribe.mode == LoadSaveMode.Saving ? AllMarkers.ToList() : null;
            Scribe_Collections.Look(ref markersFlat, "mpMarkers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Always drop stale select-times; loaded ids may overlap last-session marker ids.
                LocationPings.DropStaleSelectTimes();
                if (markersFlat != null)
                {
                    markersByFaction = new SortedDictionary<int, List<PingInfo>>();
                    foreach (var m in markersFlat)
                        GetOrCreateFactionMarkers(m.placedByFactionLoadId).Add(m);
                    markersVersion++;
                }
            }
            Scribe_Values.Look(ref nextMarkerId, "mpNextMarkerId");
            Scribe_Values.Look(ref markerCapPerPlayer, "mpMarkerCapPerPlayer", PingMarkerCap.Default);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                // Hand-edited saves can land out-of-range values.
                markerCapPerPlayer = PingMarkerCap.Clamp(markerCapPerPlayer);

            // Store for back-compat conversion in GameExposeComponentsPatch
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref idBlockBase64, "globalIdBlock");
        }

        public void WriteSessionData(ByteWriter writer)
        {
            SyncSerialization.WriteSync(writer, playerData);

            // Joiner needs host's live marker list, not the autosave - markers placed between
            // save and join would otherwise be missing, and nextMarkerId would diverge.
            SyncSerialization.WriteSync(writer, AllMarkers.ToList());
            SyncSerialization.WriteSync(writer, Math.Max(0, nextMarkerId));
            SyncSerialization.WriteSync(writer, PingMarkerCap.Clamp(markerCapPerPlayer));
        }

        public void ReadSessionData(ByteReader reader)
        {
            playerData = SyncSerialization.ReadSync<Dictionary<int, PlayerData>>(reader);
            DebugSettings.godMode = LocalPlayerDataOrNull?.godMode ?? false;

            var markersFlat = SyncSerialization.ReadSync<List<PingInfo>>(reader);
            // Negative ids would alias with legacy markerId == 0 rows.
            nextMarkerId = Math.Max(0, SyncSerialization.ReadSync<int>(reader));
            markerCapPerPlayer = PingMarkerCap.Clamp(SyncSerialization.ReadSync<int>(reader));

            // Session data is fresher than the autosave the joiner just loaded - overwrite.
            markersByFaction = new SortedDictionary<int, List<PingInfo>>();
            if (markersFlat != null)
                foreach (var m in markersFlat)
                    GetOrCreateFactionMarkers(m.placedByFactionLoadId).Add(m);
            markersVersion++;
        }

        [SyncMethod(debugOnly = true)]
        public void SetGodMode(int playerId, bool godMode)
        {
            playerData[playerId].godMode = godMode;
        }

        [SyncMethod]
        public void SetMarkerCapPerPlayer(int newCap)
        {
            markerCapPerPlayer = PingMarkerCap.Clamp(newCap);
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
