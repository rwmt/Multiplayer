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
    public class TradingWindow : Window
    {
        public static TradingWindow drawingTrade;
        public static bool cancelPressed;

        public override Vector2 InitialSize => new Vector2(1024f, UI.screenHeight);

        public TradingWindow()
        {
            doCloseX = true;
            closeOnAccept = false;
        }

        public int selectedTab = -1;
        public Dialog_Trade dialog;

        public override void DoWindowContents(Rect inRect)
        {
            Stopwatch watch = Stopwatch.StartNew();

            added.RemoveAll(kv => Time.time - kv.Value > 1f);
            removed.RemoveAll(kv => Time.time - kv.Value > 0.5f && RemoveCachedTradeable(kv.Key));

            List<TabRecord> tabs = new List<TabRecord>();
            var trading = Multiplayer.WorldComp.trading;
            for (int i = 0; i < trading.Count; i++)
            {
                int j = i;
                tabs.Add(new TabRecord(trading[i].Label, () => SelectTab(j), () => selectedTab == j));
            }

            if (selectedTab == -1 && trading.Count > 0)
                SelectTab(0);

            if (selectedTab == -1)
                Close();

            int rows = Mathf.CeilToInt(tabs.Count / 3f);
            inRect.yMin += rows * TabDrawer.TabHeight + 3;
            TabDrawer.DrawTabs(inRect, tabs, rows);

            inRect.yMin += 10f;

            if (selectedTab == -1) return;

            var session = Multiplayer.WorldComp.trading[selectedTab];

            Rect groupRect = new Rect(0, 0, inRect.width, inRect.height);
            GUI.BeginGroup(inRect);
            {
                MpTradeSession.SetTradeSession(session);
                drawingTrade = this;

                if (session.deal.ShouldRecache)
                    session.deal.Recache();

                if (session.deal.uiShouldReset != UIShouldReset.None)
                {
                    if (session.deal.uiShouldReset != UIShouldReset.Silent)
                        BeforeCache();

                    dialog.CacheTradeables();
                    dialog.CountToTransferChanged();
                    session.deal.uiShouldReset = UIShouldReset.None;
                }

                dialog.DoWindowContents(groupRect);

                if (cancelPressed)
                {
                    CancelTradeSession(session);
                    cancelPressed = false;
                }

                session.giftMode = TradeSession.giftMode;

                drawingTrade = null;
                MpTradeSession.SetTradeSession(null);
            }
            GUI.EndGroup();

            Widgets.Label(new Rect(0, 0, inRect.width, inRect.height), "" + watch.ElapsedMillisDouble());
        }

        void SelectTab(int index)
        {
            selectedTab = index;

            if (index < 0) selectedTab = -1;
            if (index >= Multiplayer.WorldComp.trading.Count) selectedTab = Multiplayer.WorldComp.trading.Count - 1;

            if (selectedTab == -1)
            {
                dialog = null;
                return;
            }

            var session = Multiplayer.WorldComp.trading[index];

            CancelDialogTradeCtor.cancel = true;
            MpTradeSession.SetTradeSession(session);

            dialog = new Dialog_Trade(null, null);
            dialog.giftsOnly = session.giftsOnly;
            dialog.sorter1 = TransferableSorterDefOf.Category;
            dialog.sorter2 = TransferableSorterDefOf.MarketValue;
            dialog.CacheTradeables();
            session.deal.uiShouldReset = UIShouldReset.None;

            removed.Clear();
            added.Clear();

            MpTradeSession.SetTradeSession(null);
            CancelDialogTradeCtor.cancel = false;
        }

        public void Notify_RemovedSession(int index)
        {
            SelectTab(selectedTab);
        }

        [SyncMethod]
        static void CancelTradeSession(MpTradeSession session)
        {
            Multiplayer.WorldComp.RemoveTradeSession(session);
        }

        public Dictionary<Tradeable, float> added = new Dictionary<Tradeable, float>();
        public Dictionary<Tradeable, float> removed = new Dictionary<Tradeable, float>();

        private bool RemoveCachedTradeable(Tradeable t)
        {
            dialog?.cachedTradeables.Remove(t);
            return true;
        }

        private static HashSet<Tradeable> newTradeables = new HashSet<Tradeable>();
        private static HashSet<Tradeable> oldTradeables = new HashSet<Tradeable>();

        private void BeforeCache()
        {
            newTradeables.AddRange(TradeSession.deal.AllTradeables);
            oldTradeables.AddRange(dialog.cachedTradeables);

            foreach (Tradeable t in newTradeables)
                if (!t.IsCurrency && !oldTradeables.Contains(t))
                    added[t] = Time.time;

            foreach (Tradeable t in oldTradeables)
                if (!t.IsCurrency && !newTradeables.Contains(t))
                    removed[t] = Time.time;

            oldTradeables.Clear();
            newTradeables.Clear();
        }

        public static IEnumerable<Tradeable> AllTradeables()
        {
            foreach (Tradeable t in TradeSession.deal.AllTradeables)
            {
                if (!TradeSession.giftMode || t.FirstThingColony != null)
                    yield return t;
            }

            if (drawingTrade != null)
            {
                foreach (var kv in drawingTrade.removed)
                    if (!TradeSession.giftMode || kv.Key.FirstThingColony != null)
                        yield return kv.Key;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade))]
    [HarmonyPatch(new[] { typeof(Pawn), typeof(ITrader), typeof(bool) })]
    static class DialogTradeCtor
    {
        public static bool running;

        static void Prefix() => running = true;
        static void Postfix() => running = false;
    }

    [MpPatch(typeof(JobDriver_TradeWithPawn), "<MakeNewToils>c__Iterator0+<MakeNewToils>c__AnonStorey1", "<>m__1")]
    static class ShowTradingWindow
    {
        public static int tradeJobStartedByMe = -1;

        static void Postfix(Toil ___trade)
        {
            if (___trade.actor.CurJob.loadID == tradeJobStartedByMe)
            {
                Find.WindowStack.Add(new TradingWindow());
                tradeJobStartedByMe = -1;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]
    static class MakeCancelTradeButtonRed
    {
        static void Prefix(string label, ref bool __state)
        {
            if (TradingWindow.drawingTrade == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        static void Postfix(bool __state)
        {
            if (__state)
                GUI.color = Color.white;
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.Close))]
    static class HandleCancelTrade
    {
        static void Prefix()
        {
            if (TradingWindow.drawingTrade != null)
                TradingWindow.cancelPressed = true;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    static class TradeDealExecutePatch
    {
        static bool Prefix(TradeDeal __instance)
        {
            if (TradingWindow.drawingTrade != null)
            {
                MpTradeSession.current.TryExecute();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.Reset))]
    static class TradeDealResetPatch
    {
        static bool Prefix(TradeDeal __instance)
        {
            if (TradingWindow.drawingTrade != null)
            {
                MpTradeSession.current.Reset();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TradeUI), nameof(TradeUI.DrawTradeableRow))]
    static class TradeableDrawPatch
    {
        static void Prefix(Tradeable trad, Rect rect)
        {
            if (TradingWindow.drawingTrade != null)
            {
                if (TradingWindow.drawingTrade.added.TryGetValue(trad, out float added))
                {
                    float alpha = 1f - (Time.time - added);
                    Widgets.DrawRectFast(rect, new Color(0, 0.4f, 0, 0.4f * alpha));
                }
                else if (TradingWindow.drawingTrade.removed.TryGetValue(trad, out float removed))
                {
                    float alpha = 1f;
                    Widgets.DrawRectFast(rect, new Color(0.4f, 0, 0, 0.4f * alpha));
                }
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.DoWindowContents))]
    static class HandleToggleGiftMode
    {
        static FieldInfo TradeModeIcon = AccessTools.Field(typeof(Dialog_Trade), nameof(Dialog_Trade.TradeModeIcon));
        static FieldInfo GiftModeIcon = AccessTools.Field(typeof(Dialog_Trade), nameof(Dialog_Trade.GiftModeIcon));

        static MethodInfo ButtonImageWithBG = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonImageWithBG));
        static MethodInfo ToggleGiftModeMethod = AccessTools.Method(typeof(HandleToggleGiftMode), nameof(ToggleGiftMode));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
        {
            List<CodeInstruction> insts = new List<CodeInstruction>(e);
            CodeFinder finder = new CodeFinder(original, insts);

            int tradeMode = finder.
                Start().
                Forward(OpCodes.Ldsfld, TradeModeIcon).
                Forward(OpCodes.Call, ButtonImageWithBG);

            insts.Insert(
                tradeMode + 2,
                new CodeInstruction(OpCodes.Call, ToggleGiftModeMethod),
                new CodeInstruction(OpCodes.Brtrue, insts[tradeMode + 1].operand)
            );

            int giftMode = finder.
                Start().
                Forward(OpCodes.Ldsfld, GiftModeIcon).
                Forward(OpCodes.Call, ButtonImageWithBG);

            insts.Insert(
                giftMode + 2,
                new CodeInstruction(OpCodes.Call, ToggleGiftModeMethod),
                new CodeInstruction(OpCodes.Brtrue, insts[giftMode + 1].operand)
            );

            return insts;
        }

        // Returns whether to jump
        static bool ToggleGiftMode()
        {
            if (TradingWindow.drawingTrade == null) return false;
            MpTradeSession.current.ToggleGiftMode();
            return true;
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.CountHeldBy))]
    static class DontShowTraderItemsInGiftMode
    {
        static void Postfix(Transactor trans, ref int __result)
        {
            if (TradingWindow.drawingTrade != null && TradeSession.giftMode && trans == Transactor.Trader)
                __result = 0;
        }
    }

    [MpPatch(typeof(Dialog_Trade), "<DoWindowContents>m__8")]
    [MpPatch(typeof(Dialog_Trade), "<DoWindowContents>m__9")]
    static class FixTradeSorters
    {
        static void Prefix(ref bool __state)
        {
            TradingWindow trading = Find.WindowStack.WindowOfType<TradingWindow>();
            if (trading != null)
            {
                MpTradeSession.SetTradeSession(Multiplayer.WorldComp.trading[trading.selectedTab]);
                __state = true;
            }
        }

        static void Postfix(bool __state)
        {
            if (__state)
                MpTradeSession.SetTradeSession(null);
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.CacheTradeables))]
    static class CacheTradeablesPatch
    {
        static void Postfix(Dialog_Trade __instance)
        {
            if (TradeSession.giftMode)
                __instance.cachedCurrencyTradeable = null;
        }

        // Replace TradeDeal.get_AllTradeables with TradingWindow.AllTradeables
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
        {
            List<CodeInstruction> insts = new List<CodeInstruction>(e);
            CodeFinder finder = new CodeFinder(original, insts);

            for (int i = 0; i < 2; i++)
            {
                int getAllTradeables = finder.Forward(OpCodes.Callvirt, AccessTools.Method(typeof(TradeDeal), "get_AllTradeables"));

                insts.RemoveRange(getAllTradeables - 1, 2);
                insts.Insert(getAllTradeables - 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TradingWindow), nameof(TradingWindow.AllTradeables))));
            }

            return insts;
        }
    }

}
