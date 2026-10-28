using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

public class FactionWorldData : IExposable
{
    public int factionId;

    public ResearchManager researchManager;
    public OutfitDatabase outfitDatabase;
    public DrugPolicyDatabase drugPolicyDatabase;
    public FoodRestrictionDatabase foodRestrictionDatabase;
    public PlaySettings playSettings;

    public History history;
    public Storyteller storyteller;
    public StoryWatcher storyWatcher;

    public AnalysisManager analysisManager;
    public FactionBossgroupData bossgroup;

    // Per-faction season notification state: each faction's transitions are
    // computed from its own min-timezone home map's async clock. See the
    // DateNotifier patch in AsyncTime/AsyncTimePatches.cs.
    public Season lastSeason;

    // Per-faction goodwill caps cache (runtime-only, rebuilds deterministically)
    // and per-NPC drift timers replacing vanilla's single OfPlayer-bound timer
    public GoodwillSituationManager goodwillSituationManager;
    public Dictionary<int, int> naturalGoodwillTimers;

    // Vanilla IdeoManager keeps these as single globals, so
    // the per-faction storyteller/thought fan turns them into cross-faction
    // bleed (one faction drains everyone's pod counter, one faction's settle
    // or psychic ritual moves every faction's precept moods). -1 = never
    // stamped for this faction; readers treat that as vanilla's default 0,
    // except the gauranlen counter, which seeds from the shared global on
    // first sim use so an existing save keeps its cycle progress.
    public int ticksToNextGauranlenSpawn = -1;
    public int lastResettledTick = -1;
    public int lastPsychicRitualPerformedTick = -1;

    // Military-aid lockout per NPC faction loadID,
    // stamped on the async world clock (CooldownClock.Now). Vanilla's single
    // Faction.lastMilitaryAidRequestTick stays written for the SP path.
    public Dictionary<int, int> militaryAidStamps;

    public FactionWorldData() { }

    public void ExposeData()
    {
        Scribe_Values.Look(ref factionId, "factionId");

        Scribe_Deep.Look(ref researchManager, "researchManager");
        Scribe_Deep.Look(ref drugPolicyDatabase, "drugPolicyDatabase");
        Scribe_Deep.Look(ref outfitDatabase, "outfitDatabase");
        Scribe_Deep.Look(ref foodRestrictionDatabase, "foodRestrictionDatabase");
        Scribe_Deep.Look(ref playSettings, "playSettings");

        Scribe_Deep.Look(ref history, "history");
        Scribe_Deep.Look(ref storyteller, "storyteller");
        Scribe_Deep.Look(ref storyWatcher, "storyWatcher");

        Scribe_Deep.Look(ref analysisManager, "analysisManager");
        Scribe_Deep.Look(ref bossgroup, "bossgroup");

        Scribe_Values.Look(ref lastSeason, "lastSeason", Season.Undefined);
        Scribe_Collections.Look(ref naturalGoodwillTimers, "naturalGoodwillTimers", LookMode.Value, LookMode.Value);

        Scribe_Values.Look(ref ticksToNextGauranlenSpawn, "ticksToNextGauranlenSpawn", -1);
        Scribe_Values.Look(ref lastResettledTick, "lastResettledTick", -1);
        Scribe_Values.Look(ref lastPsychicRitualPerformedTick, "lastPsychicRitualPerformedTick", -1);
        Scribe_Collections.Look(ref militaryAidStamps, "militaryAidStamps", LookMode.Value, LookMode.Value);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            goodwillSituationManager ??= new GoodwillSituationManager();
            naturalGoodwillTimers ??= new Dictionary<int, int>();
            militaryAidStamps ??= new Dictionary<int, int>();
            researchManager ??= new ResearchManager();
            drugPolicyDatabase ??= new DrugPolicyDatabase();
            outfitDatabase ??= new OutfitDatabase();
            foodRestrictionDatabase ??= new FoodRestrictionDatabase();
            playSettings ??= new PlaySettings();

            history ??= new History();
            storyteller ??= new Storyteller(Find.Storyteller.def, Find.Storyteller.difficultyDef,
                Find.Storyteller.difficulty);
            storyWatcher ??= new StoryWatcher();

            analysisManager ??= new AnalysisManager();
            bossgroup ??= FactionBossgroupData.New();
        }
    }

    public void ReassignIds()
    {
        foreach (DrugPolicy p in drugPolicyDatabase.policies)
            p.id = Find.UniqueIDsManager.GetNextThingID();

        foreach (ApparelPolicy o in outfitDatabase.outfits)
            o.id = Find.UniqueIDsManager.GetNextThingID();

        foreach (FoodPolicy o in foodRestrictionDatabase.foodRestrictions)
            o.id = Find.UniqueIDsManager.GetNextThingID();
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

            history = new History(),
            storyteller = new Storyteller(Find.Storyteller.def, Find.Storyteller.difficultyDef, Find.Storyteller.difficulty),
            storyWatcher = new StoryWatcher(),

            analysisManager = new AnalysisManager(),
            bossgroup = FactionBossgroupData.New(),

            goodwillSituationManager = new GoodwillSituationManager(),
            naturalGoodwillTimers = new Dictionary<int, int>(),
            militaryAidStamps = new Dictionary<int, int>()
        };
    }

    public static FactionWorldData FromCurrent(int factionId)
    {
        return new FactionWorldData()
        {
            factionId = factionId == int.MinValue ? Faction.OfPlayer.loadID : factionId,

            researchManager = Find.ResearchManager,
            drugPolicyDatabase = Current.Game.drugPolicyDatabase,
            outfitDatabase = Current.Game.outfitDatabase,
            foodRestrictionDatabase = Current.Game.foodRestrictionDatabase,
            playSettings = Current.Game.playSettings,

            history = Find.History,
            storyteller = Find.Storyteller,
            storyWatcher = Find.StoryWatcher,

            analysisManager = Current.Game.analysisManager,
            bossgroup = FactionBossgroupData.FromCurrent(),

            goodwillSituationManager = Find.FactionManager.goodwillSituationManager,
            naturalGoodwillTimers = new Dictionary<int, int>(),
            militaryAidStamps = new Dictionary<int, int>()
        };
    }
}
