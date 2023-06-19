using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

// Currently unused
public static class SyncResearch
{
    private static Dictionary<int, float> localResearch = new Dictionary<int, float>();

    //[MpPrefix(typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed))]
    static bool ResearchPerformed_Prefix(float amount, Pawn researcher)
    {
        if (Multiplayer.Client == null || !SyncMarkers.researchToil)
            return true;

        // todo only faction leader
        if (Faction.OfPlayer == Multiplayer.RealPlayerFaction)
        {
            float current = localResearch.GetValueSafe(researcher.thingIDNumber);
            localResearch[researcher.thingIDNumber] = current + amount;
        }

        return false;
    }

    // Set by faction context
    public static ResearchSpeed researchSpeed;
    public static ISyncField SyncResearchSpeed =
        Sync.Field(null, "Multiplayer.Client.SyncResearch/researchSpeed/[]").SetBufferChanges().InGameLoop();

    public static void ConstantTick()
    {
        if (localResearch.Count == 0) return;

        SyncFieldUtil.FieldWatchPrefix();

        foreach (int pawn in localResearch.Keys.ToList())
        {
            SyncResearchSpeed.Watch(null, pawn);
            researchSpeed[pawn] = localResearch[pawn];
            localResearch[pawn] = 0;
        }

        SyncFieldUtil.FieldWatchPostfix();
    }
}

public class ResearchSpeed : IExposable
{
    public Dictionary<int, float> data = new Dictionary<int, float>();

    public float this[int pawnId]
    {
        get => data.TryGetValue(pawnId, out float speed) ? speed : 0f;

        set
        {
            if (value == 0) data.Remove(pawnId);
            else data[pawnId] = value;
        }
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref data, "data", LookMode.Value, LookMode.Value);
    }
}
