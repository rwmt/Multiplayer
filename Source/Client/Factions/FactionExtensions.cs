using System.Linq;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Factions;

public static class FactionExtensions
{
    private static float lastUnwindReport;
    private static int unwoundEntries;

    // FactionContext is strict always-push/always-pop, so a push whose pop was
    // skipped (an exception inside a faction bracket) permanently shifts every
    // later pop onto the wrong entry: the restored OfPlayer and installed
    // faction data then depend on tick history instead of scope pairing, and
    // UI/render reads alternate between factions' data. Brackets record the
    // stack depth on entry and unwind to it here before their own pop -
    // popping stranded entries in LIFO order lands on the oldest one's saved
    // value, which is what the balanced path would have restored.
    public static void UnwindFactionStack(Map map, int expectedDepth, string site)
    {
        if (FactionContext.stack.Count < expectedDepth)
        {
            // Someone popped past our bracket - can't repair from here, but
            // never silent: the bracket's own pop below is now misaligned
            MpLog.Error($"Faction stack underflow at {site}: depth {FactionContext.stack.Count} < expected {expectedDepth}");
            return;
        }

        if (FactionContext.stack.Count == expectedDepth)
            return;

        unwoundEntries += FactionContext.stack.Count - expectedDepth;
        while (FactionContext.stack.Count > expectedDepth)
            map.PopFaction();

        var now = UnityEngine.Time.realtimeSinceStartup;
        if (now - lastUnwindReport < 30f) return;
        lastUnwindReport = now;
        MpLog.Warn($"Faction stack unwound: {unwoundEntries} stranded entr(y/ies), last at {site} - " +
                   "an exception inside a faction bracket skipped its pop (see errors above for the thrower)");
        unwoundEntries = 0;
    }

    // Sets the current Faction.OfPlayer
    // Applies faction's world components
    // Applies faction's map components if map not null
    public static void PushFaction(this Map map, Faction f, bool force = false)
    {
        var faction = FactionContext.Push(f, force);
        if (faction == null) return;

        Multiplayer.WorldComp?.SetFaction(faction);
        map?.MpComp().SetFaction(faction);
    }

    public static void PushFaction(this Map map, int factionId)
    {
        Faction faction = Find.FactionManager.GetById(factionId);
        map.PushFaction(faction);
    }

    public static Faction PopFaction() => PopFaction(null);

    public static Faction PopFaction(this Map map)
    {
        Faction faction = FactionContext.Pop();
        if (faction == null) return null;

        Multiplayer.WorldComp?.SetFaction(faction);
        map?.MpComp().SetFaction(faction);

        return faction;
    }

    public static bool TryGetPlayerFaction(this Quest quest, out Faction faction)
    {
        faction = quest.QuestLookTargets
            .Where(t => t.HasWorldObject && t.WorldObject is Settlement)
            .Select(t => ((Settlement)t.WorldObject).Faction)
            .FirstOrDefault(f => f != null)
            // Pawn-targeted quests (bestowing ceremonies, rescues) have no
            // settlement target - the targeted pawn's faction is the owner
            ?? quest.QuestLookTargets
                .Where(t => t.HasThing && t.Thing is Pawn)
                .Select(t => ((Pawn)t.Thing).Faction)
                .FirstOrDefault(f => f is { IsPlayer: true });

        return faction != null;
    }
}
