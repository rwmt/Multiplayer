using RimWorld;
using Verse;

namespace Multiplayer.Client;

public class FactionWorldData : IExposable
{
    public int factionId;
    public bool online;

    public ResearchManager researchManager;
    public OutfitDatabase outfitDatabase;
    public DrugPolicyDatabase drugPolicyDatabase;
    public FoodRestrictionDatabase foodRestrictionDatabase;
    public PlaySettings playSettings;

    public ResearchSpeed researchSpeed;

    public FactionWorldData() { }

    public void ExposeData()
    {
        Scribe_Values.Look(ref factionId, "factionId");
        Scribe_Values.Look(ref online, "online");

        Scribe_Deep.Look(ref researchManager, "researchManager");
        Scribe_Deep.Look(ref drugPolicyDatabase, "drugPolicyDatabase");
        Scribe_Deep.Look(ref outfitDatabase, "outfitDatabase");
        Scribe_Deep.Look(ref foodRestrictionDatabase, "foodRestrictionDatabase");
        Scribe_Deep.Look(ref playSettings, "playSettings");

        Scribe_Deep.Look(ref researchSpeed, "researchSpeed");
    }

    public void Tick()
    {
    }

    public static FactionWorldData New(int factionId)
    {
        return new FactionWorldData()
        {
            factionId = factionId,

            researchManager = new ResearchManager(),
            drugPolicyDatabase = new DrugPolicyDatabase(),
            outfitDatabase = new OutfitDatabase(),
            foodRestrictionDatabase = new FoodRestrictionDatabase(),
            playSettings = new PlaySettings(),
            researchSpeed = new ResearchSpeed(),
        };
    }

    public static FactionWorldData FromCurrent(int factionId)
    {
        return new FactionWorldData()
        {
            factionId = factionId == int.MinValue ? Faction.OfPlayer.loadID : factionId,
            online = true,

            researchManager = Find.ResearchManager,
            drugPolicyDatabase = Current.Game.drugPolicyDatabase,
            outfitDatabase = Current.Game.outfitDatabase,
            foodRestrictionDatabase = Current.Game.foodRestrictionDatabase,
            playSettings = Current.Game.playSettings,

            researchSpeed = new ResearchSpeed(),
        };
    }
}
