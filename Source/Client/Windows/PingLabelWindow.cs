using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    // Modal rename - Confirm sends ClientRenameMarkerPacket; UI updates on the server relay.
    public class PingLabelWindow : Window
    {
        private readonly int markerId;
        private string buffer;
        private bool focused;

        public override Vector2 InitialSize => new(360f, 175f);

        public PingLabelWindow(int markerId, string currentLabel)
        {
            this.markerId = markerId;
            buffer = currentLabel ?? "";
            if (buffer.Length > PingCategoryWire.MaxLabelChars)
                buffer = buffer.Substring(0, PingCategoryWire.MaxLabelChars);

            forcePause = false;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            focusWhenOpened = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float Pad = 6f;
            const float TitleH = 28f;
            const float FieldH = 28f;
            const float ButtonH = 32f;

            using (MpStyle.Set(GameFont.Medium).Set(TextAnchor.MiddleLeft))
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, TitleH), "MpPingLabel_Title".Translate());

            var fieldRect = new Rect(inRect.x, inRect.y + TitleH + Pad, inRect.width, FieldH);
            const string fieldName = "MpPingLabelField";
            GUI.SetNextControlName(fieldName);
            var next = Widgets.TextField(fieldRect, buffer, PingCategoryWire.MaxLabelChars);
            if (next != buffer) buffer = next;

            if (!focused)
            {
                UI.FocusControl(fieldName, this);
                focused = true;
            }

            var btnY = inRect.yMax - ButtonH;
            var btnW = (inRect.width - Pad) / 2f;
            if (Widgets.ButtonText(new Rect(inRect.x, btnY, btnW, ButtonH), "MpPingLabel_Cancel".Translate()))
            {
                Event.current.Use();
                Close();
                return;
            }

            var enterPressed = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (Widgets.ButtonText(new Rect(inRect.x + btnW + Pad, btnY, btnW, ButtonH), "MpPingLabel_Confirm".Translate()) || enterPressed)
            {
                if (enterPressed) Event.current.Use();
                Confirm();
            }
        }

        private void Confirm()
        {
            if (string.IsNullOrWhiteSpace(buffer))
            {
                Messages.Message("MpPingLabel_EmptyReject".Translate(),
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // Re-validate ownership - a faction switch via FactionSidebar can land while the modal
            // is open, and a stale send would silently no-op on every receiver.
            var loc = Multiplayer.session?.locationPings;
            if (loc == null) { Close(); return; }
            var marker = LocationPings.FindMarkerById(markerId);
            if (marker == null || !LocationPings.CanDeleteMarker(marker))
            {
                Messages.Message("MpPingLabel_NoLongerOwned".Translate(),
                    MessageTypeDefOf.RejectInput, historical: false);
                Close();
                return;
            }

            loc.SendRenameMarker(markerId, buffer);
            Close();
        }

    }
}
