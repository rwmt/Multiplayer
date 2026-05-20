using Multiplayer.Client.Comp;
using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    // Host-only settings popup, opened from a header button on PingMenuWindow. Owns the per-player
    // marker cap (synced game-comp field) and the two host-only "clear everything" actions.
    public class PingHostSettingsDialog : Window
    {
        public static PingHostSettingsDialog Opened => Find.WindowStack?.WindowOfType<PingHostSettingsDialog>();

        public override Vector2 InitialSize => new(360f, 260f);

        private Rect? requestedAnchor;
        private string markerCapBuffer;
        private int lastMarkerCapBufferedFor = -1;

        public PingHostSettingsDialog(Rect? anchor = null)
        {
            requestedAnchor = anchor;
            draggable = true;
            resizeable = false;
            doCloseX = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            focusWhenOpened = true;
            onlyOneOfTypeAllowed = true;
            soundClose = SoundDefOf.FloatMenu_Cancel;
            layer = WindowLayer.GameUI;
        }

        public override void SetInitialSizeAndPosition()
        {
            var size = InitialSize;
            var screen = new Vector2(UI.screenWidth, UI.screenHeight);
            const float ScreenMargin = 6f;

            Vector2 desired;
            if (requestedAnchor is { } trigger)
            {
                const float Gap = 4f;
                var belowY = trigger.yMax + Gap;
                if (belowY + size.y > screen.y - ScreenMargin)
                    desired = new Vector2(trigger.x, trigger.y - size.y - Gap);
                else
                    desired = new Vector2(trigger.x, belowY);
                requestedAnchor = null;
            }
            else
            {
                var saved = Multiplayer.settings.pingHostSettingsDialogRect;
                if (saved.width > 0f && saved.height > 0f
                    && saved.x >= -size.x + ScreenMargin && saved.x <= screen.x - ScreenMargin
                    && saved.y >= -size.y + ScreenMargin && saved.y <= screen.y - ScreenMargin)
                    desired = new Vector2(saved.x, saved.y);
                else
                    desired = new Vector2((screen.x - size.x) / 2f, (screen.y - size.y) / 2f);
            }
            var x = Mathf.Clamp(desired.x, ScreenMargin, screen.x - size.x - ScreenMargin);
            var y = Mathf.Clamp(desired.y, ScreenMargin, screen.y - size.y - ScreenMargin);
            windowRect = new Rect(x, y, size.x, size.y);
        }

        public override void PostClose()
        {
            base.PostClose();
            Multiplayer.settings.pingHostSettingsDialogRect = windowRect;
            MultiplayerLoader.Multiplayer.instance?.WriteSettings();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width - 30f, 28f);
            using (MpStyle.Set(GameFont.Medium).Set(TextAnchor.MiddleLeft))
                Widgets.Label(titleRect, "MpPingHostSettings_Title".Translate());

            var y = titleRect.yMax + 8f;

            var comp = Multiplayer.game?.gameComp;
            var loc = Multiplayer.session?.locationPings;

            if (comp != null)
            {
                const float RowH = 28f;
                const float LabelW = 180f;
                const float FieldW = 70f;

                if (lastMarkerCapBufferedFor != comp.markerCapPerPlayer)
                {
                    markerCapBuffer = comp.markerCapPerPlayer.ToString();
                    lastMarkerCapBufferedFor = comp.markerCapPerPlayer;
                }
                var capRect = new Rect(inRect.x, y, LabelW + FieldW + 8f, RowH);
                var capLabel = "MpPingMenuWindow_MarkerCapPerPlayer".Translate();
                var prevCap = comp.markerCapPerPlayer;
                var editCap = prevCap;
                MpUI.TextFieldNumericLabeled(capRect, $"{capLabel}:  ", ref editCap, ref markerCapBuffer, LabelW, PingMarkerCap.Min, PingMarkerCap.Max);
                TooltipHandler.TipRegion(capRect, "MpPingMenuWindow_MarkerCapPerPlayer_Tip".Translate(PingMarkerCap.Min, PingMarkerCap.Max));
                if (editCap != prevCap)
                {
                    comp.SetMarkerCapPerPlayer(editCap);
                    lastMarkerCapBufferedFor = editCap;
                }
                y = capRect.yMax + 12f;
            }

            Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);
            y += 6f;

            var sectionHeaderRect = new Rect(inRect.x, y, inRect.width, 18f);
            using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleLeft).Set(new Color(0.65f, 0.65f, 0.65f)))
                Widgets.Label(sectionHeaderRect, "MpPingHostSettings_ClearAllHeader".Translate());
            y = sectionHeaderRect.yMax + 4f;

            const float ButtonH = 28f;
            var clearMarkersRect = new Rect(inRect.x, y, inRect.width, ButtonH);
            if (Widgets.ButtonText(clearMarkersRect, "MpPingHostSettings_ClearAllMarkers".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "MpPingHostSettings_ClearAllMarkers_Confirm".Translate(),
                    () =>
                    {
                        loc?.SendClearAllMarkers();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    },
                    destructive: true));
            }
            y = clearMarkersRect.yMax + 4f;

            var clearPingsRect = new Rect(inRect.x, y, inRect.width, ButtonH);
            if (Widgets.ButtonText(clearPingsRect, "MpPingHostSettings_ClearAllPings".Translate()))
            {
                loc?.SendClearAllPings();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            y = clearPingsRect.yMax;
        }

        public static string OpenTooltipLabel()
            => "MpPingHostSettings_OpenTooltip".Translate();
    }
}
