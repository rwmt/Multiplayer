using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Multiplayer.Client
{
    public class MpTradeSession : ExposableSession, ISessionWithTransferables, ISessionWithCreationRestrictions, ITickingSession
    {
        public static MpTradeSession current;

        public ITrader trader;
        public Pawn playerNegotiator;
        public bool giftMode;
        public MpTradeDeal deal;
        public bool giftsOnly;

        public Faction NegotiatorFaction => playerNegotiator?.Faction;

        public string Label
        {
            get
            {
                if (trader is Pawn pawn)
                    return pawn.Faction.Name;
                return trader.TraderName;
            }
        }

        public override Map Map => playerNegotiator.Map;

        public override bool IsSessionValid => trader != null && playerNegotiator != null;

        public MpTradeSession(Map _) : base(null) { }

        private MpTradeSession(ITrader trader, Pawn playerNegotiator, bool giftMode) : base(null)
        {
            this.trader = trader;
            this.playerNegotiator = playerNegotiator;
            this.giftMode = giftMode;
            giftsOnly = giftMode;
        }

        public bool CanExistWith(Session other)
        {
            if (other is not MpTradeSession otherTrade)
                return true;

            // todo show error messages?
            if (otherTrade.trader == trader)
                return false;

            if (otherTrade.playerNegotiator == playerNegotiator)
                return false;

            return true;
        }

        public static MpTradeSession TryCreate(ITrader trader, Pawn playerNegotiator, bool giftMode)
        {
            MpTradeSession session = new MpTradeSession(trader, playerNegotiator, giftMode);
            // Return null if there was a conflicting session
            if (!Multiplayer.WorldComp.sessionManager.AddSession(session))
                return null;

            try
            {
                CancelTradeDealReset.cancel = true;
                SetTradeSession(session);

                session.deal = new MpTradeDeal(session);

                Thing permSilver = ThingMaker.MakeThing(ThingDefOf.Silver, null);
                permSilver.stackCount = 0;
                session.deal.permanentSilver = permSilver;
                session.deal.AddToTradeables(permSilver, Transactor.Trader);

                session.deal.AddAllTradeables();
                session.StartWaitingJobs();
            }
            finally
            {
                SetTradeSession(null);
                CancelTradeDealReset.cancel = false;
            }

            return session;
        }

        // todo come back to it when the map doesn't get paused during trading
        private void StartWaitingJobs()
        {
        }

        public bool ShouldCancel()
        {
            if (!trader.CanTradeNow)
                return true;

            if (trader is Pawn traderPawn)
            {
                if (!traderPawn.Spawned || !playerNegotiator.Spawned)
                    return true;
                return traderPawn.Position.DistanceToSquared(playerNegotiator.Position) > 2 * 2;
            }

            if (trader is Settlement traderBase)
            {
                var caravan = playerNegotiator.GetCaravan();
                if (caravan == null)
                    return true;

                if (CaravanVisitUtility.SettlementVisitedNow(caravan) != traderBase)
                    return true;
            }

            return false;
        }

        [SyncMethod]
        public void TryExecute()
        {
            bool executed = false;

            try
            {
                SetTradeSession(this);

                deal.recacheColony = true;
                deal.recacheTrader = true;
                deal.Recache();

                executed = deal.TryExecute(out bool traded);
            }
            finally
            {
                SetTradeSession(null);
            }

            if (executed)
                Multiplayer.WorldComp.RemoveTradeSession(this);
        }

        [SyncMethod]
        public void Reset()
        {
            deal.tradeables.ForEach(t => t.countToTransfer = 0);
            deal.uiShouldReset = UIShouldReset.Silent;
        }

        [SyncMethod]
        public void ToggleGiftMode()
        {
            giftMode = !giftMode;
            deal.tradeables.ForEach(t => t.countToTransfer = 0);
            deal.uiShouldReset = UIShouldReset.Silent;
        }

        public static void SetTradeSession(MpTradeSession session)
        {
            SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = session;
            current = session;
            TradeSession.trader = session?.trader;
            TradeSession.playerNegotiator = session?.playerNegotiator;
            TradeSession.giftMode = session?.giftMode ?? false;
            TradeSession.deal = session?.deal;
        }

        public void OpenWindow(bool sound = true)
        {
            int tab = Multiplayer.WorldComp.trading.IndexOf(this);
            if (Find.WindowStack.IsOpen<TradingWindow>())
            {
                Find.WindowStack.WindowOfType<TradingWindow>().selectedTab = tab;
            }
            else
            {
                TradingWindow window = new TradingWindow() { selectedTab = tab };
                if (!sound)
                    window.soundAppear = null;
                Find.WindowStack.Add(window);
            }
        }

        public void CloseWindow(bool sound = true)
        {
            int tab = Multiplayer.WorldComp.trading.IndexOf(this);
            if (Find.WindowStack.IsOpen<TradingWindow>())
            {
                Find.WindowStack.TryRemove(typeof(TradingWindow), doCloseSound: sound);
            }
        }

        public void Tick()
        {
            if (playerNegotiator.Spawned) return;

            if (ShouldCancel())
                Multiplayer.WorldComp.sessionManager.RemoveSession(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            ILoadReferenceable trader = (ILoadReferenceable)this.trader;
            Scribe_References.Look(ref trader, "trader");
            this.trader = (ITrader)trader;

            Scribe_References.Look(ref playerNegotiator, "playerNegotiator");
            Scribe_Values.Look(ref giftMode, "giftMode");
            Scribe_Values.Look(ref giftsOnly, "giftsOnly");

            Scribe_Deep.Look(ref deal, "tradeDeal", this);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Multiplayer.WorldComp.trading.AddDistinct(this);
        }

        public Transferable GetTransferableByThingId(int thingId)
        {
            for (int i = 0; i < deal.tradeables.Count; i++)
            {
                Tradeable tr = deal.tradeables[i];
                if (tr.FirstThingColony?.thingIDNumber == thingId)
                    return tr;
                if (tr.FirstThingTrader?.thingIDNumber == thingId)
                    return tr;
            }

            return null;
        }

        public void Notify_CountChanged(Transferable tr)
        {
            deal.caravanDirty = true;
        }

        public override bool IsCurrentlyPausing(Map map) => map == Map;

        public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
        {
            if (playerNegotiator?.Map != entry.map)
                return null;

            return new FloatMenuOption("MpTradingSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                CameraJumper.TryJumpAndSelect(playerNegotiator);
                Find.WindowStack.Add(new TradingWindow()
                    { selectedTab = Multiplayer.WorldComp.trading.IndexOf(this) });
            });
        }

        public override void PostAddSession() => Multiplayer.WorldComp.trading.Add(this);

        public override void PostRemoveSession()
        {
            var index = Multiplayer.WorldComp.trading.IndexOf(this);
            Multiplayer.WorldComp.trading.RemoveAt(index);
            Find.WindowStack?.WindowOfType<TradingWindow>()?.Notify_RemovedSession(index);
        }
    }


    public class MpTradeDeal : TradeDeal, IExposable
    {
        public MpTradeSession session;

        private static HashSet<Thing> newThings = new HashSet<Thing>();
        private static HashSet<Thing> oldThings = new HashSet<Thing>();

        public UIShouldReset uiShouldReset;

        public HashSet<Thing> recacheThings = new HashSet<Thing>();
        public bool recacheColony;
        public bool recacheTrader;
        public bool ShouldRecache => recacheColony || recacheTrader || recacheThings.Count > 0;
        public bool caravanDirty;

        public Thing permanentSilver;

        public MpTradeDeal(MpTradeSession session)
        {
            this.session = session;
        }

        public void Recache()
        {
            if (recacheColony)
                CheckAddRemoveColony();

            if (recacheTrader)
                CheckAddRemoveTrader();

            if (recacheThings.Count > 0)
                CheckReassign();

            UpdateCurrencyCount();

            uiShouldReset = UIShouldReset.Full;
            recacheThings.Clear();
            recacheColony = false;
            recacheTrader = false;
        }

        private void CheckAddRemoveColony()
        {
            foreach (Thing t in session.trader.ColonyThingsWillingToBuy(session.playerNegotiator))
                newThings.Add(t);

            for (int i = tradeables.Count - 1; i >= 0; i--)
            {
                Tradeable tradeable = tradeables[i];
                int toRemove = 0;

                for (int j = tradeable.thingsColony.Count - 1; j >= 0; j--)
                {
                    Thing thingColony = tradeable.thingsColony[j];
                    if (!newThings.Contains(thingColony))
                        toRemove++;
                    else
                        oldThings.Add(thingColony);
                }

                if (toRemove == 0) continue;

                if (toRemove == tradeable.thingsColony.Count + tradeable.thingsTrader.Count)
                    tradeables.RemoveAt(i);
                else
                    tradeable.thingsColony.RemoveAll(t => !newThings.Contains(t));
            }

            foreach (Thing newThing in newThings)
                if (!oldThings.Contains(newThing))
                    AddToTradeables(newThing, Transactor.Colony);

            newThings.Clear();
            oldThings.Clear();
        }

        private void CheckAddRemoveTrader()
        {
            newThings.Add(permanentSilver);

            foreach (Thing t in session.trader.Goods)
                newThings.Add(t);

            for (int i = tradeables.Count - 1; i >= 0; i--)
            {
                Tradeable tradeable = tradeables[i];
                int toRemove = 0;

                for (int j = tradeable.thingsTrader.Count - 1; j >= 0; j--)
                {
                    Thing thingTrader = tradeable.thingsTrader[j];
                    if (!newThings.Contains(thingTrader))
                        toRemove++;
                    else
                        oldThings.Add(thingTrader);
                }

                if (toRemove == 0) continue;

                if (toRemove == tradeable.thingsColony.Count + tradeable.thingsTrader.Count)
                    tradeables.RemoveAt(i);
                else
                    tradeable.thingsTrader.RemoveAll(t => !newThings.Contains(t));
            }

            foreach (Thing newThing in newThings)
                if (!oldThings.Contains(newThing))
                    AddToTradeables(newThing, Transactor.Trader);

            newThings.Clear();
            oldThings.Clear();
        }

        private void CheckReassign()
        {
            for (int i = tradeables.Count - 1; i >= 0; i--)
            {
                Tradeable tradeable = tradeables[i];

                CheckReassign(tradeable, Transactor.Colony);
                CheckReassign(tradeable, Transactor.Trader);

                if (recacheThings.Count == 0) break;
            }
        }

        private void CheckReassign(Tradeable tradeable, Transactor side)
        {
            List<Thing> things = side == Transactor.Colony ? tradeable.thingsColony : tradeable.thingsTrader;

            for (int j = things.Count - 1; j >= 1; j--)
            {
                Thing thing = things[j];
                TransferAsOneMode mode = tradeable.TraderWillTrade ? TransferAsOneMode.Normal : TransferAsOneMode.InactiveTradeable;

                if (recacheThings.Contains(thing))
                {
                    if (!TransferableUtility.TransferAsOne(tradeable.AnyThing, thing, mode))
                        things.RemoveAt(j);
                    else
                        AddToTradeables(thing, side);
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref permanentSilver, "permanentSilver");
            Scribe_Collections.Look(ref tradeables, "tradeables", LookMode.Deep);
        }
    }

    public enum UIShouldReset
    {
        None,
        Silent,
        Full
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.Reset))]
    static class CancelTradeDealReset
    {
        public static bool cancel;

        static bool Prefix() => !cancel && Scribe.mode != LoadSaveMode.LoadingVars;
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class CancelDialogTrade
    {
        static bool Prefix(Window window)
        {
            if (window is Dialog_Trade && (Multiplayer.ExecutingCmds || Multiplayer.Ticking))
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), MethodType.Constructor)]
    [HarmonyPatch(new[] { typeof(Pawn), typeof(ITrader), typeof(bool) })]
    static class DialogTradeCtorPatch
    {
        static bool Prefix(Pawn playerNegotiator, ITrader trader, bool giftsOnly)
        {
            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                MpTradeSession trade = MpTradeSession.TryCreate(trader, playerNegotiator, giftsOnly);

                if (trade != null)
                {
                    if (playerNegotiator.Map == Find.CurrentMap && playerNegotiator.CurJob.loadID == SyncMethods.tradeJobStartedByMe)
                    {
                        SyncMethods.tradeJobStartedByMe = -1;
                        trade.OpenWindow();
                    }
                    else if (trader is Settlement && Find.World.renderer.wantedMode == WorldRenderMode.Planet)
                    {
                        trade.OpenWindow();
                    }
                }

                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival), nameof(IncidentWorker_TraderCaravanArrival.TryExecuteWorker))]
    static class ArriveAtCenter
    {
        static void Prefix(IncidentParms parms)
        {
            //if (MpVersion.IsDebug && Prefs.DevMode)
            //    parms.spawnCenter = (parms.target as Map).Center;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    static class NullCheckDialogTrade
    {
        static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> e)
        {
            List<CodeInstruction> insts = new List<CodeInstruction>(e);
            LocalBuilder local = gen.DeclareLocal(typeof(Dialog_Trade));

            for (int i = 0; i < insts.Count; i++)
            {
                CodeInstruction inst = insts[i];
                yield return inst;

                if (inst.opcode == OpCodes.Callvirt && ((MethodInfo)inst.operand).Name == nameof(WindowStack.WindowOfType))
                {
                    Label label = gen.DefineLabel();
                    insts[i + 2].labels.Add(label);

                    yield return new CodeInstruction(OpCodes.Stloc, local);
                    yield return new CodeInstruction(OpCodes.Ldloc, local);
                    yield return new CodeInstruction(OpCodes.Brfalse, label);
                    yield return new CodeInstruction(OpCodes.Ldloc, local);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Reachability), nameof(Reachability.ClearCache))]
    static class ReachabilityChanged
    {
        static void Postfix(Reachability __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.map);
        }
    }

    [HarmonyPatch(typeof(Area_Home), nameof(Area_Home.Set))]
    static class AreaHomeChanged
    {
        static void Postfix(Area_Home __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.Map);
        }
    }

    [HarmonyPatch]
    static class HaulDestinationChanged
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(HaulDestinationManager), nameof(HaulDestinationManager.AddHaulDestination));
            yield return AccessTools.Method(typeof(HaulDestinationManager), nameof(HaulDestinationManager.RemoveHaulDestination));
            yield return AccessTools.Method(typeof(HaulDestinationManager), nameof(HaulDestinationManager.SetCellFor));
            yield return AccessTools.Method(typeof(HaulDestinationManager), nameof(HaulDestinationManager.ClearCellFor));
        }
        static void Postfix(HaulDestinationManager __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.map);
        }
    }

    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.StageChanged))]
    static class RottableStageChanged
    {
        static void Postfix(CompRottable __instance)
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.parent.Map);
        }
    }

    [HarmonyPatch]
    static class ListerThingsChangedItem
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Add));
            yield return AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Remove));
        }
        static void Postfix(ListerThings __instance, Thing t)
        {
            if (Multiplayer.Client == null) return;
            if (t.def.category == ThingCategory.Item && ListerThings.EverListable(t.def, __instance.use))
                Multiplayer.WorldComp.DirtyColonyTradeForMap(t.Map);
        }
    }

    [HarmonyPatch]
    static class PawnDownedStateChanged
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeDowned));
            yield return AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeUndowned));
        }
        static void Postfix(Pawn_HealthTracker __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.pawn.Map);
        }
    }

    [HarmonyPatch(typeof(CompPowerTrader))]
    [HarmonyPatch(nameof(CompPowerTrader.PowerOn), MethodType.Setter)]
    static class OrbitalTradeBeaconPowerChanged
    {
        static void Postfix(CompPowerTrader __instance, bool value)
        {
            if (Multiplayer.Client == null) return;
            if (__instance.parent is not Building_OrbitalTradeBeacon) return;
            if (value == __instance.powerOnInt) return;
            if (!Multiplayer.WorldComp.trading.Any(t => t.trader is TradeShip)) return;

            // For trade ships
            Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.parent.Map);
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.HitPoints), MethodType.Setter)]
    static class ThingHitPointsChanged
    {
        static void Prefix(Thing __instance, int value, ref bool __state)
        {
            if (Multiplayer.Client == null) return;
            __state = __instance.def.category == ThingCategory.Item && value != __instance.hitPointsInt;
        }

        static void Postfix(Thing __instance, bool __state)
        {
            if (__state)
                Multiplayer.WorldComp.DirtyTradeForSpawnedThing(__instance);
        }
    }

    [HarmonyPatch]
    static class ThingOwner_ChangedPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.NotifyAdded));
            yield return AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.NotifyAddedAndMergedWith));
            yield return AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.NotifyRemoved));
        }
        static void Postfix(ThingOwner __instance)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.owner is Pawn_InventoryTracker inv)
            {
                ITrader trader = null;

                if (inv.pawn.GetLord()?.LordJob is LordJob_TradeWithColony lordJob)
                    // Carrier inventory changed
                    trader = lordJob.lord.ownedPawns.FirstOrDefault(p => p.GetTraderCaravanRole() == TraderCaravanRole.Trader);
                else if (inv.pawn.trader != null)
                    // Trader inventory changed
                    trader = inv.pawn;

                if (trader != null)
                    Multiplayer.WorldComp.DirtyTraderTradeForTrader(trader);
            }
            else if (__instance.owner is Settlement_TraderTracker trader)
            {
                Multiplayer.WorldComp.DirtyTraderTradeForTrader(trader.settlement);
            }
            else if (__instance.owner is TradeShip ship)
            {
                Multiplayer.WorldComp.DirtyTraderTradeForTrader(ship);
            }
        }
    }

    [HarmonyPatch]
    static class Lord_TradeChanged
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Lord), nameof(Lord.AddPawn));
            yield return AccessTools.Method(typeof(Lord), nameof(Lord.Notify_PawnLost));
        }
        static void Postfix(Lord __instance)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.LordJob is LordJob_TradeWithColony)
            {
                // Chattel changed
                ITrader trader = __instance.ownedPawns.FirstOrDefault(p => p.GetTraderCaravanRole() == TraderCaravanRole.Trader);
                Multiplayer.WorldComp.DirtyTraderTradeForTrader(trader);
            }
            else if (__instance.LordJob is LordJob_PrisonBreak)
            {
                // Prisoners in a break can't be sold
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.Map);
            }
        }
    }

    [HarmonyPatch]
    static class MentalStateChanged
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState));
            yield return AccessTools.Method(typeof(MentalStateHandler), nameof(MentalStateHandler.ClearMentalStateDirect));
        }
        static void Postfix(MentalStateHandler __instance)
        {
            if (Multiplayer.Client == null) return;

            // Pawns in a mental state can't be sold
            Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.pawn.Map);
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Notify_Starting))]
    static class JobExitMapStarted
    {
        static void Postfix(JobDriver __instance)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.job.exitMapOnArrival)
            {
                // Prisoners exiting the map can't be sold
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.pawn.Map);
            }
        }
    }

    [HarmonyPatch(typeof(Settlement_TraderTracker), nameof(Settlement_TraderTracker.TraderTrackerTick))]
    static class DontDestroyStockWhileTrading
    {
        static bool Prefix(Settlement_TraderTracker __instance)
        {
            return Multiplayer.Client == null || Multiplayer.WorldComp.trading.All(s => s.trader != __instance.settlement);
        }
    }

    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.DoListChangedNotifications))]
    static class MapPawnsChanged
    {
        static void Postfix(MapPawns __instance)
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.map);
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.RecalculateLifeStageIndex))]
    static class PawnLifeStageChanged
    {
        static void Postfix(Pawn_AgeTracker __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!__instance.pawn.Spawned) return;

            Multiplayer.WorldComp.DirtyTradeForSpawnedThing(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeTick))]
    static class PawnAgeChanged
    {
        static void Prefix(Pawn_AgeTracker __instance, ref int __state)
        {
            __state = __instance.AgeBiologicalYears;
        }

        static void Postfix(Pawn_AgeTracker __instance, int __state)
        {
            if (Multiplayer.Client == null) return;
            if (__state == __instance.AgeBiologicalYears) return;

            // todo?
        }
    }

    [HarmonyPatch(typeof(TransferableUtility), nameof(TransferableUtility.TransferAsOne))]
    static class TransferAsOneAgeCheck_Patch
    {
        static MethodInfo AgeBiologicalFloat = AccessTools.PropertyGetter(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeBiologicalYearsFloat));
        static MethodInfo AgeBiologicalInt = AccessTools.PropertyGetter(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeBiologicalYears));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == AgeBiologicalFloat)
                {
                    yield return new CodeInstruction(OpCodes.Callvirt, AgeBiologicalInt);
                    yield return new CodeInstruction(OpCodes.Conv_R4);
                    continue;
                }

                yield return inst;
            }
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.InSellablePosition))]
    static class InSellablePositionPatch
    {
        // todo actually handle this
        static void Postfix(Thing t, ref bool __result, ref string reason)
        {
            if (Multiplayer.Client == null) return;

            //__result = t.Spawned;
            //reason = null;
        }
    }
}
