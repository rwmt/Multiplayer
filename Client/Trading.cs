using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public class MpTradeSession
    {
        public static MpTradeSession current;

        public int sessionId;
        public ITrader trader;
        public Pawn playerNegotiator;
        public bool giftMode;
        public MpTradeDeal deal;
        public bool giftsOnly;

        public bool startedWaitJobs;

        public string Label
        {
            get
            {
                if (trader is Pawn pawn)
                    return pawn.Faction.Name;
                return trader.TraderName;
            }
        }

        public MpTradeSession() { }

        private MpTradeSession(ITrader trader, Pawn playerNegotiator, bool giftMode)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();

            this.trader = trader;
            this.playerNegotiator = playerNegotiator;
            this.giftMode = giftMode;
            giftsOnly = giftMode;

            SetTradeSession(this, true);
            deal = new MpTradeDeal(this);
            SetTradeSession(null);
        }

        public static void TryCreate(ITrader trader, Pawn playerNegotiator, bool giftMode)
        {
            if (Multiplayer.WorldComp.trading.Any(s => s.trader == trader))
                return;

            if (Multiplayer.WorldComp.trading.Any(s => s.playerNegotiator == playerNegotiator))
                return;

            Multiplayer.WorldComp.trading.Add(new MpTradeSession(trader, playerNegotiator, giftMode));
        }

        public bool ShouldCancel()
        {
            if (!trader.CanTradeNow)
                return true;

            if (playerNegotiator.Drafted)
                return true;

            if (trader is Pawn pawn && pawn.Spawned && playerNegotiator.Spawned)
                return pawn.Position.DistanceToSquared(playerNegotiator.Position) > 2 * 2;

            return false;
        }

        public void TryExecute()
        {
            SetTradeSession(this);
            deal.TryExecute(out bool traded);
            SetTradeSession(null);

            Multiplayer.WorldComp.RemoveTradeSession(this);
        }

        public void Reset()
        {
            deal.tradeables.ForEach(t => t.countToTransfer = 0);
            deal.uiShouldReset = UIShouldReset.Silent;
        }

        public void ToggleGiftMode()
        {
            giftMode = !giftMode;
            deal.tradeables.ForEach(t => t.countToTransfer = 0);
            deal.uiShouldReset = UIShouldReset.Silent;
        }

        public Tradeable GetTradeableByThingId(int thingId)
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

        public static void SetTradeSession(MpTradeSession session, bool force = false)
        {
            if (!force && TradeSession.deal == session?.deal) return;

            current = session;
            TradeSession.trader = session?.trader;
            TradeSession.playerNegotiator = session?.playerNegotiator;
            TradeSession.giftMode = session?.giftMode ?? false;
            TradeSession.deal = session?.deal;
        }
    }

    public class MpTradeDeal : TradeDeal
    {
        public MpTradeSession session;

        private static HashSet<Thing> newThings = new HashSet<Thing>();
        private static HashSet<Thing> oldThings = new HashSet<Thing>();

        public UIShouldReset uiShouldReset;

        public HashSet<Thing> recacheThings = new HashSet<Thing>();
        public bool fullRecache;
        public bool ShouldRecache => fullRecache || recacheThings.Count > 0;

        public MpTradeDeal(MpTradeSession session)
        {
            this.session = session;
        }

        public void Recache()
        {
            if (fullRecache)
                CheckAddRemove();

            if (recacheThings.Count > 0)
                CheckReassign();

            newThings.Clear();
            oldThings.Clear();

            uiShouldReset = UIShouldReset.Full;
            recacheThings.Clear();
            fullRecache = false;
        }

        private void CheckAddRemove()
        {
            foreach (Thing t in TradeSession.trader.ColonyThingsWillingToBuy(TradeSession.playerNegotiator))
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
        }

        private void CheckReassign()
        {
            for (int i = tradeables.Count - 1; i >= 0; i--)
            {
                Tradeable tradeable = tradeables[i];
                for (int j = tradeable.thingsColony.Count - 1; j >= 1; j--)
                {
                    Thing thingColony = tradeable.thingsColony[j];
                    TransferAsOneMode mode = (!tradeable.TraderWillTrade) ? TransferAsOneMode.InactiveTradeable : TransferAsOneMode.Normal;

                    if (recacheThings.Contains(thingColony))
                    {
                        if (!TransferableUtility.TransferAsOne(tradeable.AnyThing, thingColony, mode))
                            tradeable.thingsColony.RemoveAt(j);
                        else
                            AddToTradeables(thingColony, Transactor.Colony);
                    }
                }

                if (recacheThings.Count == 0) break;
            }
        }
    }

    public enum UIShouldReset
    {
        None,
        Silent,
        Full
    }

    [HarmonyPatch(typeof(JobDriver_Wait), nameof(JobDriver_Wait.DecorateWaitToil))]
    static class TradingWaitJobToil
    {
        static void Postfix(Toil wait)
        {
            wait.AddPreTickAction(() =>
            {
                Job job = wait.actor.CurJob;
                if (job.count == 1234 && Multiplayer.WorldComp.trading.Any(s => s.playerNegotiator == wait.actor || s.trader == wait.actor))
                {
                    job.startTick = Find.TickManager.TicksGame + 1; // Don't expire while trading
                }
            });
        }
    }

    [HarmonyPatch(typeof(JobDriver_Wait), nameof(JobDriver_Wait.GetReport))]
    static class TradingWaitJobReport
    {
        static void Postfix(JobDriver_Wait __instance, ref string __result)
        {
            if (__instance.job.count == 1234)
                __result = "Negotiating trade";
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival), nameof(IncidentWorker_TraderCaravanArrival.TryExecuteWorker))]
    static class ArriveAtCenter
    {
        static void Prefix(IncidentParms parms)
        {
            parms.spawnCenter = (parms.target as Map).Center;
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
                Multiplayer.WorldComp.DirtyTradeForMaps(__instance.map);
        }
    }

    [HarmonyPatch(typeof(Area_Home), nameof(Area_Home.Set))]
    static class AreaHomeChanged
    {
        static void Postfix(Area_Home __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyTradeForMaps(__instance.Map);
        }
    }

    [MpPatch(typeof(HaulDestinationManager), nameof(HaulDestinationManager.AddHaulDestination))]
    [MpPatch(typeof(HaulDestinationManager), nameof(HaulDestinationManager.RemoveHaulDestination))]
    [MpPatch(typeof(HaulDestinationManager), nameof(HaulDestinationManager.SetCellFor))]
    [MpPatch(typeof(HaulDestinationManager), nameof(HaulDestinationManager.ClearCellFor))]
    static class HaulDestinationChanged
    {
        static void Postfix(HaulDestinationManager __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyTradeForMaps(__instance.map);
        }
    }

    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.StageChanged))]
    static class RottableStageChanged
    {
        static void Postfix(CompRottable __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyTradeForMaps(__instance.parent.Map);
        }
    }

    [MpPatch(typeof(ListerThings), nameof(ListerThings.Add))]
    [MpPatch(typeof(ListerThings), nameof(ListerThings.Remove))]
    static class ListerThingsChangedItem
    {
        static void Postfix(ListerThings __instance, Thing t)
        {
            if (Multiplayer.Client == null) return;
            if (t.def.category == ThingCategory.Item && ListerThings.EverListable(t.def, __instance.use))
                Multiplayer.WorldComp.DirtyTradeForMaps(t.Map);
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.HitPoints), PropertyMethod.Setter)]
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
                Multiplayer.WorldComp.DirtyTradeForThing(__instance);
        }
    } 

}
