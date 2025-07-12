using Multiplayer.Client.Patches;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.DebugUi
{
    public struct StatusBadge
    {
        public string icon;
        public Color color;
        public string text;
        public string tooltip;

        public StatusBadge(string icon, Color color, string text, string tooltip)
        {
            this.icon = icon;
            this.color = color;
            this.text = text;
            this.tooltip = tooltip;
        }

        public static StatusBadge GetSyncStatus()
        {
            if (Multiplayer.session == null)
                return new StatusBadge("●", Color.yellow, "N/A", "No active session");

            if (Multiplayer.session.desynced)
                return new StatusBadge("●", Color.red, "DESYNC", "Session has desynced");

            return new StatusBadge("●", Color.green, "SYNC", "All players are synchronized");
        }

        public static StatusBadge GetPerformanceStatus()
        {
            float tps = IngameUIPatch.tps;
            string tooltip = tps > 40f ? "Performance is good" : tps > 20f ? "Performance is moderate" : "Performance is poor";

            return new StatusBadge("▲", GetPerformanceColor(tps, 40f, 20f), $"{tps:F1}", tooltip);
        }

        public static StatusBadge GetTickStatus()
        {
            int behind = TickPatch.tickUntil - TickPatch.Timer;
            Color color = GetPerformanceColor(behind, 5, 15, true);
            string tooltip = behind <= 5 ? "Timing is good" : behind <= 15 ? "Slightly behind" : "Significantly behind";
            return new StatusBadge("♦", color, behind.ToString(), tooltip);
        }

        public static StatusBadge GetVtrStatus()
        {
            int rate = Find.CurrentMap?.AsyncTime()?.VTR ?? VTRSync.MaximumVtr;
            return new StatusBadge("V", rate == 15 ? Color.red : Color.green, rate.ToString(), $"Variable Tick Rate: Things update every {rate} tick(s)");
        }

        public static StatusBadge GetNumOfPlayersStatus()
        {
            int playerCount = Find.CurrentMap?.AsyncTime()?.CurrentPlayerCount ?? 0;
            return new StatusBadge("P", playerCount > 0 ? Color.green : Color.red, $"{playerCount}", "Active players in the current map");
        }

        // Unified color logic for performance metrics
        public static Color GetPerformanceColor(float value, float goodThreshold, float moderateThreshold, bool lowerIsBetter = false)
        {
            if (lowerIsBetter)
                return value <= goodThreshold ? Color.green : value < moderateThreshold ? Color.yellow : Color.red;

            return value >= goodThreshold ? Color.green : value >= moderateThreshold ? Color.yellow : Color.red;
        }
    }
} 
