using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Patches
{
    // Heal-and-tell for faction data residue. Sim brackets (map tick, world
    // tick, command execution) swap the live world/map managers between
    // factions' copies; any strand (an exception that skipped a pop, a
    // restore that no-op'd) leaves foreign faction data installed, and
    // between frames NOTHING re-installs the local player's - alerts read a
    // foreign resourceCounter (low-food/medicine flips and re-rings), zones/
    // areas/designations render from the wrong copies, and a paused map
    // stays wrong until its next tick ("clears on unpause"). The bracket
    // depth guards close the strand sources we know; this closes the class:
    // after the tick/command loop, before render and UI read anything,
    // re-point every map and the world at the local faction's data and name
    // what was wrong. Render/UI-only by construction - every sim path
    // installs its own context on entry, so healed state is never a
    // simulation input.
    public static class FactionResidueGuard
    {
        private static int healedFrames;
        private static float lastReport;
        private const float ReportIntervalSeconds = 60f;

        public static void HealAfterTicks()
        {
            if (Multiplayer.Client == null || Multiplayer.game == null || Multiplayer.reloading) return;
            if (!Multiplayer.GameComp.multifaction) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            var real = Multiplayer.RealPlayerFaction;
            if (real == null) return;

            string healed = null;

            // Outside any open bracket the ambient player faction must be the
            // local player's; a non-empty stack after the tick loop means
            // something stranded - the depth guards report that separately
            if (FactionContext.stack.Count == 0 && Faction.OfPlayer != real)
            {
                healed = $"OfPlayer was {Faction.OfPlayer?.loadID.ToString() ?? "null"}";
                FactionContext.Set(real);
            }

            var worldComp = Multiplayer.WorldComp;
            if (worldComp.factionData.TryGetValue(real.loadID, out var worldData) &&
                !ReferenceEquals(Find.ResearchManager, worldData.researchManager))
            {
                healed = "world data was foreign";
                worldComp.SetFaction(real);
            }

            // Every map, not just the viewed one: alerts and the colonist bar
            // read all maps' managers each frame
            foreach (var map in Find.Maps)
            {
                var comp = map.MpComp();
                if (comp == null || !comp.factionData.TryGetValue(real.loadID, out var ownData)) continue;
                if (ReferenceEquals(map.resourceCounter, ownData.resourceCounter)) continue;

                var installedFactionId = -1;
                foreach (var kv in comp.factionData)
                    if (ReferenceEquals(map.resourceCounter, kv.Value.resourceCounter))
                        installedFactionId = kv.Key;

                comp.SetFaction(real);
                healed = $"map {map.uniqueID} had faction {installedFactionId}'s data";
            }

            if (healed == null) return;

            healedFrames++;
            var now = Time.realtimeSinceStartup;
            if (now - lastReport < ReportIntervalSeconds) return;
            lastReport = now;

            MpLog.Warn(
                $"Faction residue healed: {healed} -> local {real.loadID} (OfPlayer {Faction.OfPlayer?.loadID}); " +
                $"{healedFrames} healed frames since last report");
            healedFrames = 0;
        }
    }
}
