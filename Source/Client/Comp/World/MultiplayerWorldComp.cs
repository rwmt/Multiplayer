using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Saving;
using Multiplayer.Common;

namespace Multiplayer.Client;

public class MultiplayerWorldComp : IHasSessionData
{
    // SortedDictionary to ensure determinism
    public SortedDictionary<int, FactionWorldData> factionData = new();

    public World world;

    public TileTemperaturesComp uiTemperatures;
    public List<MpTradeSession> trading = new(); // Should only be modified from MpTradeSession in PostAdd/Remove and ExposeData
    public SessionManager sessionManager = new(null);

    public Faction spectatorFaction;

    // Multifaction: quest.id -> faction.loadID owning the quest (see QuestFactionOwnership)
    public Dictionary<int, int> questOwnership = new();

    // Multifaction: wastepack thingIDNumber -> faction.loadID of the dumper.
    // Consulted when a spawned pack rots on a non-player-home map; pruned on
    // destroy. See Factions/WastepackAttribution.cs.
    public Dictionary<int, int> wastepackDumpers = new();

    // Multifaction: per-faction ritual repeat-penalty windows.
    // Key = (precept.Id << 32) | faction.loadID -> finish tick (see IdeoSharedStatePatches)
    public Dictionary<long, int> ritualLastFinished = new();

    private int currentFactionId;

    public MultiplayerWorldComp(World world)
    {
        this.world = world;
        uiTemperatures = new TileTemperaturesComp(world);
    }

    // Called from AsyncWorldTimeComp.ExposeData (for backcompat)
    public void ExposeData()
    {
        ExposeFactionData();

        Scribe_Collections.Look(ref questOwnership, "questOwnership", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref ritualLastFinished, "ritualLastFinished", LookMode.Value, LookMode.Value);
        if (Scribe.mode != LoadSaveMode.Saving)
            questOwnership ??= new Dictionary<int, int>();

        Scribe_Collections.Look(ref wastepackDumpers, "wastepackDumpers", LookMode.Value, LookMode.Value);
        if (Scribe.mode != LoadSaveMode.Saving)
        {
            wastepackDumpers ??= new Dictionary<int, int>();
            ritualLastFinished ??= new Dictionary<long, int>();
        }

        sessionManager.ExposeSessions();
        // Ensure a pause lock session exists if there's any pause locks registered
        if (!PauseLockSession.pauseLocks.NullOrEmpty() && Scribe.mode == LoadSaveMode.PostLoadInit)
            sessionManager.AddSession(new PauseLockSession(null));

        DoBackCompat();
    }

    private void DoBackCompat()
    {
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
            return;

        if (spectatorFaction == null)
        {
            void AddSpectatorFaction()
            {
                spectatorFaction = HostUtil.AddNewFaction("Spectator", FactionDefOf.PlayerColony);

                factionData[spectatorFaction.loadID] = FactionWorldData.New(spectatorFaction.loadID);
                factionData[spectatorFaction.loadID].ReassignIds();

                foreach (var map in Find.Maps)
                    MapSetup.InitNewFactionData(map, spectatorFaction);
            }

            void RemoveOpponentFaction()
            {
                // Test faction left in by mistake in version 0.7
                var opponent =
                    Find.FactionManager.AllFactions.FirstOrDefault(f => f.Name == "Opponent" && f.IsPlayer);

                if (opponent is not null)
                {
                    opponent.RemoveAllRelations();
                    Find.FactionManager.allFactions.Remove(opponent);
                    Log.Warning("Multiplayer removed dummy Opponent faction");
                }
            }

            AddSpectatorFaction();
            RemoveOpponentFaction();
        }

        if (Multiplayer.GameComp.multifaction)
            Factions.QuestFactionOwnership.BackfillOwnership();

        // Fix old save files by ensuring all factions have access to Anomaly research if
        // it was enabled. This needs to be done since Anomaly state is shared by all players.
        var anomaly = Find.Anomaly;
        // Don't use anomaly.Level, as it'll return 0 due to monolith not being
        // spawned. If we want to include that check then we'd need to move this
        // code into a postfix to Building_VoidMonolith:SpawnSetup.
        if (anomaly.level > 0 && anomaly.monolith != null)
        {
            foreach (var (_, data) in factionData)
                data.researchManager.Notify_MonolithLevelChanged(anomaly.level);
        }
    }

    private void ExposeFactionData()
    {
        Scribe_References.Look(ref spectatorFaction, "spectatorFaction");

        if (Scribe.mode == LoadSaveMode.Saving)
        {
            int currentFactionId = GetFactionId(Find.ResearchManager);
            Scribe_Custom.LookValue(currentFactionId, "currentFactionId");

            var savedFactionData = new SortedDictionary<int, FactionWorldData>(factionData);
            savedFactionData.Remove(currentFactionId);

            Scribe_Custom.LookValueDeep(ref savedFactionData, "factionData");
        }
        else
        {
            // The faction whose data is currently set
            Scribe_Values.Look(ref currentFactionId, "currentFactionId");

            Scribe_Custom.LookValueDeep(ref factionData, "factionData");
            factionData ??= new SortedDictionary<int, FactionWorldData>();
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars && Multiplayer.session != null && Multiplayer.game != null)
        {
            Multiplayer.game.myFactionLoading =
                Find.FactionManager.GetById(Multiplayer.session.myFactionId) ?? spectatorFaction;
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            // Game manager order?
            factionData[currentFactionId] = FactionWorldData.FromCurrent(currentFactionId);
        }
    }

    public void WriteSessionData(ByteWriter writer)
    {
        sessionManager.WriteSessionData(writer);
    }

    public void ReadSessionData(ByteReader data)
    {
        sessionManager.ReadSessionData(data);
    }

    public void TickWorldSessions()
    {
        sessionManager.TickSessions();
    }

    public void RemoveTradeSession(MpTradeSession session)
    {
        // Cleanup and removal from `trading` field is handled in PostRemoveSession
        sessionManager.RemoveSession(session);
    }

    public void SetFaction(Faction faction)
    {
        if (!factionData.TryGetValue(faction.loadID, out FactionWorldData data))
            return;

        Game game = Current.Game;
        game.researchManager = data.researchManager;
        game.drugPolicyDatabase = data.drugPolicyDatabase;
        game.outfitDatabase = data.outfitDatabase;
        game.foodRestrictionDatabase = data.foodRestrictionDatabase;
        game.playSettings = data.playSettings;

        game.history = data.history;
        game.storyteller = data.storyteller;
        game.storyWatcher = data.storyWatcher;

        if (data.analysisManager != null)
            game.analysisManager = data.analysisManager;

        // Goodwill caps/natural-goodwill cache per faction: workers read
        // Faction.OfPlayer's ideo, so each faction's queries hit its own instance
        if (Multiplayer.GameComp.multifaction && data.goodwillSituationManager != null)
            Find.FactionManager.goodwillSituationManager = data.goodwillSituationManager;

        // Bossgroup component state: dict/list swap by reference, the cooldown
        // int is copied in (written back by BossgroupLastCalledWriteBack).
        // Multifaction only - in plain MP the vanilla component stays live and
        // this would stamp a stale cooldown over it every forced context push
        if (Multiplayer.GameComp.multifaction &&
            data.bossgroup != null && game.GetComponent<GameComponent_Bossgroup>() is { } bossgroups)
        {
            bossgroups.timesCalledBossgroups = data.bossgroup.timesCalledBossgroups;
            bossgroups.killedBosses = data.bossgroup.killedBosses;
            bossgroups.lastBossgroupCalled = data.bossgroup.lastBossgroupCalled;
        }
    }

    public void DirtyColonyTradeForMap(Map map)
    {
        if (map == null) return;
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == map)
                session.deal.recacheColony = true;
    }

    public void DirtyTraderTradeForTrader(ITrader trader)
    {
        if (trader == null) return;
        foreach (MpTradeSession session in trading)
            if (session.trader == trader)
                session.deal.recacheTrader = true;
    }

    public void DirtyTradeForSpawnedThing(Thing t)
    {
        if (t is not { Spawned: true }) return;
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == t.Map)
                session.deal.recacheThings.Add(t);
    }

    public bool AnyTradeSessionsOnMap(Map map)
    {
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == map)
                return true;
        return false;
    }

    public int GetFactionId(ResearchManager researchManager)
    {
        return factionData.First(kv => kv.Value.researchManager == researchManager).Key;
    }

    public override string ToString()
    {
        return $"{nameof(MultiplayerWorldComp)}_{world}";
    }
}
