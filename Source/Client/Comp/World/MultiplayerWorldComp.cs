using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Saving;

namespace Multiplayer.Client;

public class MultiplayerWorldComp : IHasSemiPersistentData
{
    public Dictionary<int, FactionWorldData> factionData = new();

    public World world;

    public TileTemperaturesComp uiTemperatures;
    public SessionManager sessionManager;

    public Faction spectatorFaction;

    private int currentFactionId;

    public MultiplayerWorldComp(World world)
    {
        this.world = world;
        uiTemperatures = new TileTemperaturesComp(world);
        sessionManager = new SessionManager(this);
    }

    // Called from AsyncWorldTimeComp.ExposeData (for backcompat)
    public void ExposeData()
    {
        ExposeFactionData();

        Scribe_References.Look(ref spectatorFaction, "spectatorFaction");

        sessionManager.ExposeSessions();

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
    }

    private void ExposeFactionData()
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            int currentFactionId = Faction.OfPlayer.loadID;
            Scribe_Custom.LookValue(currentFactionId, "currentFactionId");

            var factionData = new Dictionary<int, FactionWorldData>(this.factionData);
            factionData.Remove(currentFactionId);

            Scribe_Collections.Look(ref factionData, "factionData", LookMode.Value, LookMode.Deep);
        }
        else
        {
            // The faction whose data is currently set
            Scribe_Values.Look(ref currentFactionId, "currentFactionId");

            Scribe_Collections.Look(ref factionData, "factionData", LookMode.Value, LookMode.Deep);
            factionData ??= new Dictionary<int, FactionWorldData>();
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars && Multiplayer.session != null && Multiplayer.game != null)
        {
            Multiplayer.game.myFactionLoading = Find.FactionManager.GetById(Multiplayer.session.myFactionId);
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            // Game manager order?
            factionData[currentFactionId] = FactionWorldData.FromCurrent(currentFactionId);
        }
    }

    public void WriteSemiPersistent(ByteWriter writer)
    {
        sessionManager.WriteSemiPersistent(writer);
    }

    public void ReadSemiPersistent(ByteReader data)
    {
        sessionManager.ReadSemiPersistent(data);
    }

    public void TickWorldSessions()
    {
        sessionManager.TickSessions();
    }

    public void RemoveTradeSession(MpTradeSession session)
    {
        int index = trading.IndexOf(session);
            trading.Remove(session);
            Find.WindowStack?.WindowOfType<TradingWindow>()?.Notify_RemovedSession(index);
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

        SyncResearch.researchSpeed = data.researchSpeed;
    }

    public void DirtyColonyTradeForMap(Map map)
    {
        if (map == null) return;
        foreach (MpTradeSession session in sessionManager.AllSessions.OfType<MpTradeSession>())
            if (session.playerNegotiator.Map == map)
                session.deal.recacheColony = true;
    }

    public void DirtyTraderTradeForTrader(ITrader trader)
    {
        if (trader == null) return;
        foreach (MpTradeSession session in sessionManager.AllSessions.OfType<MpTradeSession>())
            if (session.trader == trader)
                session.deal.recacheTrader = true;
    }

    public void DirtyTradeForSpawnedThing(Thing t)
    {
        if (t is not { Spawned: true }) return;
        foreach (MpTradeSession session in sessionManager.AllSessions.OfType<MpTradeSession>())
            if (session.playerNegotiator.Map == t.Map)
                session.deal.recacheThings.Add(t);
    }

    public bool AnyTradeSessionsOnMap(Map map)
    {
        foreach (MpTradeSession session in sessionManager.AllSessions.OfType<MpTradeSession>())
            if (session.playerNegotiator.Map == map)
                return true;
        return false;
    }

    public override string ToString()
    {
        return $"{nameof(MultiplayerWorldComp)}_{world}";
    }
}
