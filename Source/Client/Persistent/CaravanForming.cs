using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Windows;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class CaravanFormingSession : IExposable, ISessionWithTransferables
    {
        public Map map;

        public int sessionId;
        public bool reform;
        public Action onClosed;
        public bool mapAboutToBeRemoved;
        public int startingTile = -1;
        public int destinationTile = -1;
        public List<TransferableOneWay> transferables;

        public bool uiDirty;

        public int SessionId => sessionId;

        public CaravanFormingSession(Map map)
        {
            this.map = map;
        }

        public CaravanFormingSession(Map map, bool reform, Action onClosed, bool mapAboutToBeRemoved) : this(map)
        {
            //sessionId = map.MpComp().mapIdBlock.NextId();
            sessionId = Multiplayer.GlobalIdBlock.NextId();

            this.reform = reform;
            this.onClosed = onClosed;
            this.mapAboutToBeRemoved = mapAboutToBeRemoved;

            AddItems();
        }

        private void AddItems()
        {
            var dialog = new MpFormingCaravanWindow(map, reform, null, mapAboutToBeRemoved);
            dialog.autoSelectTravelSupplies = false;
            dialog.CalculateAndRecacheTransferables();
            transferables = dialog.transferables;
        }

        public void OpenWindow(bool sound = true)
        {
            Find.Selector.ClearSelection();

            var dialog = PrepareDummyDialog();
            if (!sound)
                dialog.soundAppear = null;

            CaravanUIUtility.CreateCaravanTransferableWidgets(
                transferables,
                out dialog.pawnsTransfer,
                out dialog.itemsTransfer,
                out dialog.travelSuppliesTransfer,
                "FormCaravanColonyThingCountTip".Translate(),
                dialog.IgnoreInventoryMode,
                () => dialog.MassCapacity - dialog.MassUsage,
                dialog.AutoStripSpawnedCorpses,
                dialog.CurrentTile,
                mapAboutToBeRemoved
            );

            dialog.CountToTransferChanged();

            Find.WindowStack.Add(dialog);
        }

        private MpFormingCaravanWindow PrepareDummyDialog()
        {
            var dialog = new MpFormingCaravanWindow(map, reform, null, mapAboutToBeRemoved)
            {
                transferables = transferables,
                startingTile = startingTile,
                destinationTile = destinationTile,
                thisWindowInstanceEverOpened = true,
                autoSelectTravelSupplies = false,
            };

            return dialog;
        }

        [SyncMethod]
        public void ChooseRoute(int destinationTile)
        {
            var dialog = PrepareDummyDialog();
            dialog.Notify_ChoseRoute(destinationTile);

            startingTile = dialog.startingTile;
            this.destinationTile = dialog.destinationTile;

            uiDirty = true;
        }

        [SyncMethod]
        public void TryReformCaravan()
        {
            if (PrepareDummyDialog().TryReformCaravan())
                Remove();
        }

        [SyncMethod]
        public void TryFormAndSendCaravan()
        {
            if (PrepareDummyDialog().TryFormAndSendCaravan())
                Remove();
        }

        [SyncMethod(debugOnly = true)]
        public void DebugTryFormCaravanInstantly()
        {
            if (PrepareDummyDialog().DebugTryFormCaravanInstantly())
                Remove();
        }
        
        [SyncMethod]
        public void Reset()
        {
            transferables.ForEach(t => t.CountToTransfer = 0);
            uiDirty = true;
        }
 
        [SyncMethod]
        public void Remove()
        {
            map.MpComp().caravanForming = null;
            Find.WorldRoutePlanner.Stop();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref reform, "reform");
            Scribe_Values.Look(ref onClosed, "onClosed");
            Scribe_Values.Look(ref mapAboutToBeRemoved, "mapAboutToBeRemoved");
            Scribe_Values.Look(ref startingTile, "startingTile");
            Scribe_Values.Look(ref destinationTile, "destinationTile");

            Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
        }

        public Transferable GetTransferableByThingId(int thingId)
        {
            return transferables.Find(tr => tr.things.Any(t => t.thingIDNumber == thingId));
        }

        public void Notify_CountChanged(Transferable tr)
        {
            uiDirty = true;
        }
    }

    public class MpFormingCaravanWindow : Dialog_FormCaravan
    {
        public static MpFormingCaravanWindow drawing;

        public CaravanFormingSession Session => map.MpComp().caravanForming;

        public static float initialWidth = 1024f;
        public static float initialHeight = UI.screenHeight * 0.90f;
        public override Vector2 InitialSize => new Vector2(initialWidth, initialHeight);

        public MpFormingCaravanWindow(Map map, bool reform = false, Action onClosed = null, bool mapAboutToBeRemoved = false) : base(map, reform, onClosed, mapAboutToBeRemoved)
        {
            doCloseX = true;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            draggable = true;
            resizeable = true;
            resizer = new WindowResizer();
            resizer.minWindowSize = new Vector2(initialWidth, initialHeight * 0.5f);
        }

        public override void DoWindowContents(Rect inRect)
        {
            drawing = this;

            try
            {
                var session = Session;

                if (session == null)
                {
                    Close();
                }
                else if (session.uiDirty)
                {
                    CountToTransferChanged();
                    startingTile = session.startingTile;
                    destinationTile = session.destinationTile;

                    session.uiDirty = false;
                }

                base.DoWindowContents(inRect);
            }
            finally
            {
                drawing = null;
            }
        }

        public override void Notify_ResolutionChanged()
        {
            this.KeepWindowOnScreen();
        }

        public override void PostOpen()
        {
            base.PostOpen();
            this.RestoreWindowSize();
        }

        public override void PostClose()
        {
            base.PostClose();
            this.SaveWindowRect();
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]
    static class MakeCancelFormingButtonRed
    {
        static void Prefix(string label, ref bool __state)
        {
            if (MpFormingCaravanWindow.drawing == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        static void Postfix(bool __state, ref bool __result)
        {
            if (!__state) return;

            GUI.color = Color.white;
            if (__result)
            {
                MpFormingCaravanWindow.drawing.Session?.Remove();
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]
    static class FormCaravanHandleReset
    {
        static void Prefix(string label, ref bool __state)
        {
            if (MpFormingCaravanWindow.drawing == null) return;
            if (label != "ResetButton".Translate()) return;

            __state = true;
        }

        static void Postfix(bool __state, ref bool __result)
        {
            if (!__state) return;

            if (__result)
            {
                MpFormingCaravanWindow.drawing.Session?.Reset();
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.DrawAutoSelectCheckbox))]
    static class DrawAutoSelectCheckboxPatch
    {
        // TODO: Sync autoSelectFoodAndMedicine
        // This is merely hidding it and enabling manual transfer as a side effect.
        static bool Prefix(Dialog_FormCaravan __instance, Rect rect) {
            if (Multiplayer.ShouldSync && __instance is MpFormingCaravanWindow dialog)
            {
                rect.yMin += 37f;
                rect.height = 35f;

                bool autoSelectFoodAndMedicine = false;

                Widgets.CheckboxLabeled(rect, "AutomaticallySelectFoodAndMedicine".Translate(), ref autoSelectFoodAndMedicine, disabled: true, placeCheckboxNearText: true);

                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.TryFormAndSendCaravan))]
    static class TryFormAndSendCaravanPatch
    {
        static bool Prefix(Dialog_FormCaravan __instance)
        {
            if (Multiplayer.ShouldSync && __instance is MpFormingCaravanWindow dialog)
            {
                dialog.Session?.TryFormAndSendCaravan();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.DebugTryFormCaravanInstantly))]
    static class DebugTryFormCaravanInstantlyPatch
    {
        static bool Prefix(Dialog_FormCaravan __instance)
        {
            if (Multiplayer.ShouldSync && __instance is MpFormingCaravanWindow dialog)
            {
                dialog.Session?.DebugTryFormCaravanInstantly();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.TryReformCaravan))]
    static class TryReformCaravanPatch
    {
        static bool Prefix(Dialog_FormCaravan __instance)
        {
            if (Multiplayer.ShouldSync && __instance is MpFormingCaravanWindow dialog)
            {
                dialog.Session?.TryReformCaravan();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.Notify_ChoseRoute))]
    static class Notify_ChoseRoutePatch
    {
        static bool Prefix(Dialog_FormCaravan __instance, int destinationTile)
        {
            if (Multiplayer.ShouldSync && __instance is MpFormingCaravanWindow dialog)
            {
                dialog.Session?.ChooseRoute(destinationTile);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class CancelDialogFormCaravan
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.MapContext != null && window.GetType() == typeof(Dialog_FormCaravan))
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), MethodType.Constructor)]
    [HarmonyPatch(new[] { typeof(Map), typeof(bool), typeof(Action), typeof(bool) })]
    static class CancelDialogFormCaravanCtor
    {
        static bool Prefix(Dialog_FormCaravan __instance, Map map, bool reform, Action onClosed, bool mapAboutToBeRemoved)
        {
            if (__instance.GetType() != typeof(Dialog_FormCaravan))
                return true;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                var comp = map.MpComp();
                if (comp.caravanForming == null)
                    comp.CreateCaravanFormingSession(reform, onClosed, mapAboutToBeRemoved);

                return true;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TimedForcedExit), nameof(TimedForcedExit.CompTick))]
    static class TimedForcedExitTickPatch
    {
        static bool Prefix(TimedForcedExit __instance)
        {
            if (Multiplayer.Client != null && __instance.parent is MapParent mapParent && mapParent.HasMap)
                return !mapParent.Map.AsyncTime().Paused;

            return true;
        }
    }

}
