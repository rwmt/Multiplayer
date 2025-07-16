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
            
            if (PerformanceCalculator.IsInStabilizationPeriod())
            {
                return new StatusBadge("▲", Color.yellow, "STAB", "Stabilizing after speed change");
            }
            
            float normalizedTps = PerformanceCalculator.GetNormalizedTPS(tps);
            
            string tooltip = normalizedTps >= 90f ? "Performance is excellent" : 
                            normalizedTps >= 70f ? "Performance is good" : 
                            normalizedTps >= 50f ? "Performance is moderate" : 
                            normalizedTps >= 25f ? "Performance is poor" : 
                            "Performance is very poor";
            
            return new StatusBadge("▲", PerformanceCalculator.GetPerformanceColor(normalizedTps, 90f, 70f), $"{normalizedTps:F0}%", tooltip);
        }


        public static StatusBadge GetTickStatus()
        {
            int behind = TickPatch.tickUntil - TickPatch.Timer;
            Color color = PerformanceCalculator.GetPerformanceColor(behind, 5, 15, true);
            string tooltip = behind <= 5 ? "Timing is good" : behind <= 15 ? "Slightly behind" : "Significantly behind";
            return new StatusBadge("♦", color, behind.ToString(), tooltip);
        }

        public static StatusBadge GetVtrStatus()
        {
            int rate = Find.CurrentMap?.AsyncTime()?.VTR ?? Patches.VTRSync.MaximumVtr;
            return new StatusBadge("V", rate == 15 ? Color.red : Color.green, rate.ToString(), $"Variable Tick Rate: Things update every {rate} tick(s)");
        }

        public static StatusBadge GetNumOfPlayersStatus()
        {
            int playerCount = Find.CurrentMap?.AsyncTime()?.CurrentPlayerCount ?? 0;
            return new StatusBadge("P", playerCount > 0 ? Color.green : Color.red, $"{playerCount}", "Active players in the current map");
        }

    }
} 
