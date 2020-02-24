using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    [HotSwappable]
    public class ChatWindow : Window
    {
        public static ChatWindow Opened => Find.WindowStack?.WindowOfType<ChatWindow>();

        public override Vector2 InitialSize => new Vector2(640f, 460f);

        private static readonly Texture2D SelectedMsg = SolidColorMaterials.NewSolidColorTexture(new Color(0.17f, 0.17f, 0.17f, 0.85f));

        private Vector2 chatScroll;
        private Vector2 playerListScroll;
        private Vector2 steamScroll;
        private float messagesHeight;
        private string currentMsg = "";
        private bool hasBeenFocused;

        public override float Margin => 0f;

        public ChatWindow()
        {
            absorbInputAroundWindow = false;
            draggable = true;
            soundClose = null;
            preventCameraMotion = false;
            focusWhenOpened = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            resizeable = true;
            onlyOneOfTypeAllowed = true;
            closeOnCancel = false;

            if (!Multiplayer.session.desynced)
            {
                layer = WindowLayer.GameUI;
                doWindowBackground = !MultiplayerMod.settings.transparentChat;
                drawShadow = doWindowBackground;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (MultiplayerMod.settings.transparentChat && !Mouse.IsOver(inRect))
            {
                GUI.contentColor = new Color(1, 1, 1, 0.8f);
                GUI.backgroundColor = new Color(1, 1, 1, 0.8f);
            }

            if (!drawShadow)
                Widgets.DrawShadowAround(inRect);

            if (Widgets.CloseButtonFor(inRect))
                Close();

            inRect = inRect.ContractedBy(18f);

            Multiplayer.session.hasUnread = false;

            Text.Font = GameFont.Small;

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Return)
                {
                    SendMsg();
                    Event.current.Use();
                }
            }

            const float infoWidth = 140f;
            Rect chat = new Rect(inRect.x, inRect.y, inRect.width - infoWidth - 10f, inRect.height);
            DrawChat(chat);

            GUI.BeginGroup(new Rect(chat.xMax + 10f, chat.y, infoWidth, inRect.height));
            DrawInfo(new Rect(0, 0, infoWidth, inRect.height));
            GUI.EndGroup();

            if (KeyBindingDefOf.Cancel.KeyDownEvent && Find.WindowStack.focusedWindow == this)
            {
                Close(true);
                Event.current.Use();
            }
        }

        private void DrawInfo(Rect inRect)
        {
            Widgets.Label(inRect, Multiplayer.session.gameName);
            inRect.yMin += 30f;

            DrawList(
                "MpChatPlayers".Translate(Multiplayer.session.players.Count),
                Multiplayer.session.players,
                p => $"{p.username} ({p.latency}ms)",
                ref inRect,
                ref playerListScroll,
                ClickPlayer,
                extra: (p, rect) =>
                {
                    if (p.type == PlayerType.Steam)
                    {
                        var steamIcon = new Rect(rect.xMax - 24f, 0, 24f, 24f);
                        rect.width -= 24f;
                        GUI.DrawTexture(steamIcon, ContentSourceUtility.ContentSourceIcon_SteamWorkshop);
                        TooltipHandler.TipRegion(steamIcon, new TipSignal($"{p.steamPersonaName}\n{p.steamId}", p.id));
                    }

                    string toolTip = $"{p.ticksBehind >> 1} ticks behind";
                    if ((p.ticksBehind & 1) != 0)
                        toolTip += "\n(Simulating)";

                    TooltipHandler.TipRegion(rect, new TipSignal(toolTip, p.id));
                },
                entryLabelColor: e => GetColor(e.status)
            );

            inRect.yMin += 10f;

            DrawList(
                "MpSteamAcceptTitle".Translate(),
                Multiplayer.session.pendingSteam,
                SteamFriends.GetFriendPersonaName,
                ref inRect,
                ref steamScroll,
                AcceptSteamPlayer,
                true,
                "MpSteamAcceptDesc".Translate()
            );
        }

        private void ClickPlayer(PlayerInfo p)
        {
            if (p.id == 0 && Event.current.button == 1)
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>()
                {
                    new FloatMenuOption("MpSeeModList".Translate(), () => DefMismatchWindow.ShowModList(Multiplayer.session.mods))
                }));
            }
        }

        private void AcceptSteamPlayer(CSteamID id)
        {
            SteamNetworking.AcceptP2PSessionWithUser(id);
            Multiplayer.session.pendingSteam.Remove(id);

            Messages.Message("MpSteamAccepted".Translate(), MessageTypeDefOf.PositiveEvent, false);
        }

        private Color GetColor(PlayerStatus status)
        {
            switch (status)
            {
                case PlayerStatus.Simulating: return new Color(1, 1, 1, 0.6f);
                case PlayerStatus.Desynced: return new Color(0.8f, 0.4f, 0.4f, 0.8f);
                default: return new Color(1, 1, 1, 0.8f);
            }
        }

        private void DrawList<T>(string label, IList<T> entries, Func<T, string> entryString, ref Rect inRect, ref Vector2 scroll, Action<T> click = null, bool hideEmpty = false, string tooltip = null, Action<T, Rect> extra = null, Func<T, Color?> entryLabelColor = null)
        {
            if (hideEmpty && entries.Count == 0) return;

            Widgets.Label(inRect, label);
            inRect.yMin += 20f;

            float entryHeight = 24f;
            float height = entries.Count * entryHeight;

            Rect outRect = new Rect(0, inRect.yMin, inRect.width, Math.Min(height, Math.Min(230, inRect.height)));
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, height);
            if (viewRect.height <= outRect.height)
                viewRect.width += 16f;

            Widgets.BeginScrollView(outRect, ref scroll, viewRect, true);
            GUI.color = new Color(1, 1, 1, 0.8f);

            float y = height;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                y -= entryHeight;

                T entry = entries[i];
                string entryLabel = entryString(entry);

                var entryRect = new Rect(0, y, viewRect.width, entryHeight);
                GUI.BeginGroup(entryRect);
                entryRect = entryRect.AtZero();

                if (i % 2 == 0)
                    Widgets.DrawAltRect(entryRect);

                if (Mouse.IsOver(entryRect))
                {
                    GUI.DrawTexture(entryRect, SelectedMsg);
                    if (click != null && Event.current.type == EventType.MouseUp)
                    {
                        click(entry);
                        Event.current.Use();
                    }
                }

                if (tooltip != null)
                    TooltipHandler.TipRegion(entryRect, tooltip);

                var prevColor = GUI.color;
                var labelColor = entryLabelColor?.Invoke(entry);
                if (labelColor != null)
                    GUI.color = labelColor.Value;

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect, entryLabel);
                Text.Anchor = TextAnchor.UpperLeft;

                if (labelColor != null)
                    GUI.color = prevColor;

                extra?.Invoke(entry, entryRect);

                GUI.EndGroup();
            }

            GUI.color = Color.white;
            Widgets.EndScrollView();

            inRect.yMin += outRect.height;
        }

        private void DrawChat(Rect inRect)
        {
            if ((Event.current.type == EventType.MouseDown || KeyBindingDefOf.Cancel.KeyDownEvent) && !GUI.GetNameOfFocusedControl().NullOrEmpty())
            {
                Event.current.Use();
                UI.UnfocusCurrentControl();
            }

            Rect outRect = new Rect(0f, 0f, inRect.width, inRect.height - 30f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, messagesHeight + 10f);

            float width = viewRect.width;
            Rect textField = new Rect(20f, outRect.yMax + 5f, width - 70f, 25f);

            GUI.BeginGroup(inRect);

            GUI.SetNextControlName("chat_input");
            currentMsg = Widgets.TextField(textField, currentMsg);
            currentMsg = currentMsg.Substring(0, Math.Min(currentMsg.Length, ServerPlayingState.MaxChatMsgLength));

            Widgets.BeginScrollView(outRect, ref chatScroll, viewRect);

            float yPos = 0;
            GUI.color = Color.white;

            int i = 0;

            foreach (ChatMsg msg in Multiplayer.session.messages)
            {
                float height = Text.CalcHeight(msg.Msg, width - 20f);
                float textWidth = Text.CalcSize(msg.Msg).x + 15;
                Rect msgRect = new Rect(20f, yPos, width - 20f, height);

                if (Mouse.IsOver(msgRect))
                {
                    GUI.DrawTexture(msgRect, SelectedMsg);

                    if (msg.TimeStamp != null)
                        TooltipHandler.TipRegion(msgRect, msg.TimeStamp.ToLongTimeString());
                }

                Color cursorColor = GUI.skin.settings.cursorColor;
                GUI.skin.settings.cursorColor = new Color(0, 0, 0, 0);

                msgRect.width = Math.Min(textWidth, msgRect.width);
                bool mouseOver = Mouse.IsOver(msgRect);

                if (mouseOver && msg.Clickable)
                    GUI.color = new Color(0.8f, 0.8f, 1);

                GUI.SetNextControlName("chat_msg_" + i++);
                Widgets.TextArea(msgRect, msg.Msg, true);

                if (mouseOver && msg.Clickable)
                {
                    GUI.color = Color.white;

                    if (Event.current.type == EventType.MouseUp)
                        msg.Click();
                }

                GUI.skin.settings.cursorColor = cursorColor;

                yPos += height;
            }

            if (Event.current.type == EventType.Layout)
                messagesHeight = yPos;

            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(textField.xMax + 5f, textField.y, 55f, textField.height), "MpSend".Translate()))
                SendMsg();

            GUI.EndGroup();

            if (!hasBeenFocused && Event.current.type == EventType.Repaint)
            {
                chatScroll.y = messagesHeight;

                GUI.FocusControl("chat_input");
                TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                editor.OnFocus();
                editor.MoveTextEnd();
                hasBeenFocused = true;
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            hasBeenFocused = false;
        }

        public void SendMsg()
        {
            currentMsg = currentMsg.Trim();

            if (currentMsg.NullOrEmpty()) return;

            if (MpVersion.IsDebug && currentMsg == "/netinfo")
                Find.WindowStack.Add(new DebugTextWindow(NetInfoText()));
            else if (Multiplayer.Client == null)
                Multiplayer.session.AddMsg(Multiplayer.username + ": " + currentMsg);
            else
                Multiplayer.Client.Send(Packets.Client_Chat, currentMsg);

            currentMsg = "";
        }

        private string NetInfoText()
        {
            if (Multiplayer.session == null) return "";

            var text = new StringBuilder();

            var netClient = Multiplayer.session.netClient;
            if (netClient != null)
            {
                text.AppendLine($"Bytes received: {netClient.Statistics.BytesReceived}");
                text.AppendLine($"Bytes sent: {netClient.Statistics.BytesSent}");
                text.AppendLine($"Packets received: {netClient.Statistics.PacketsReceived}");
                text.AppendLine($"Packets sent: {netClient.Statistics.PacketsSent}");
                text.AppendLine($"Packet loss percent: {netClient.Statistics.PacketLossPercent}");
                text.AppendLine();
            }

            foreach (var remote in Multiplayer.session.knownUsers)
            {
                text.AppendLine(SteamFriends.GetFriendPersonaName(remote));
                text.AppendLine(remote.ToString());

                if (SteamNetworking.GetP2PSessionState(remote, out P2PSessionState_t state))
                {
                    text.AppendLine($"Active: {state.m_bConnectionActive}");
                    text.AppendLine($"Connecting: {state.m_bConnecting}");
                    text.AppendLine($"Error: {state.m_eP2PSessionError}");
                    text.AppendLine($"Using relay: {state.m_bUsingRelay}");
                    text.AppendLine($"Bytes to send: {state.m_nBytesQueuedForSend}");
                    text.AppendLine($"Packets to send: {state.m_nPacketsQueuedForSend}");
                    text.AppendLine($"Remote IP: {state.m_nRemoteIP}");
                    text.AppendLine($"Remote port: {state.m_nRemotePort}");
                }
                else
                {
                    text.AppendLine("No connection");
                }

                text.AppendLine();
            }

            return text.ToString();
        }

        public void OnChatReceived()
        {
            chatScroll.y = messagesHeight;
        }

        public override void PostClose()
        {
            if (Multiplayer.session != null)
                Multiplayer.session.chatPos = windowRect;
        }
    }

    public abstract class ChatMsg
    {
        public virtual bool Clickable => false;
        public abstract string Msg { get; }
        public virtual DateTime TimeStamp => timestamp;

        private DateTime timestamp;

        public ChatMsg()
        {
            timestamp = DateTime.Now;
        }

        public virtual void Click() { }
    }

    public class ChatMsg_Text : ChatMsg
    {
        private string msg;
        public override string Msg => msg;

        public ChatMsg_Text(string msg)
        {
            this.msg = msg;
        }
    }

    public class ChatMsg_Url : ChatMsg
    {
        public override string Msg => url;
        public override bool Clickable => true;

        private string url;

        public ChatMsg_Url(string url)
        {
            this.url = url;
        }

        public override void Click()
        {
            Application.OpenURL(url);
        }
    }

}
