using Multiplayer.Common;
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
    public class ChatWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);

        private static readonly Texture2D SelectedMsg = SolidColorMaterials.NewSolidColorTexture(new Color(0.17f, 0.17f, 0.17f, 0.85f));

        private Vector2 chatScroll;
        private Vector2 playerListScroll;
        private Vector2 steamScroll;
        private float messagesHeight;
        private List<ChatMsg> messages = new List<ChatMsg>();
        private string currentMsg = "";
        private bool hasBeenFocused;

        public ChatWindow()
        {
            absorbInputAroundWindow = false;
            draggable = true;
            soundClose = null;
            preventCameraMotion = false;
            focusWhenOpened = true;
            doCloseX = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            resizeable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
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
        }

        private void DrawInfo(Rect inRect)
        {
            DrawOptions(ref inRect);

            Widgets.Label(inRect, Multiplayer.Client != null ? "Connected" : "Not connected");
            inRect.yMin += 30f;

            DrawList(
                $"Players ({Multiplayer.session.players.Count}):", 
                Multiplayer.session.players, 
                p => $"{p.username} ({p.latency}ms)", 
                ref inRect, 
                ref playerListScroll
            );

            inRect.yMin += 10f;

            DrawList(
                "Accept Steam players", 
                Multiplayer.session.pendingSteam,
                SteamFriends.GetFriendPersonaName, 
                ref inRect, 
                ref steamScroll, 
                AcceptSteamPlayer, 
                true, 
                "Click to accept"
            );
        }

        private void DrawOptions(ref Rect inRect)
        {
            if (Multiplayer.LocalServer == null) return;

            const float height = 20f;

            if (SteamManager.Initialized)
            {
                // todo sync this between players
                string label = "Steam";
                Rect optionsRect = new Rect(inRect.width, inRect.yMax - height, 0, height);
                optionsRect.xMin -= Text.CalcSize(label).x + 24f + 10f;
                Widgets.CheckboxLabeled(optionsRect, label, ref Multiplayer.session.allowSteam);
                inRect.yMax -= 20;
            }

            {
                string label = "LAN";
                Rect optionsRect = new Rect(inRect.width, inRect.yMax - height, 0, height);
                optionsRect.xMin -= Text.CalcSize(label).x + 24f + 10f;

                bool allowLan = Multiplayer.LocalServer.allowLan;
                Widgets.CheckboxLabeled(optionsRect, label, ref allowLan);
                Multiplayer.LocalServer.allowLan = allowLan;

                inRect.yMax -= 20;
            }
        }

        private void AcceptSteamPlayer(CSteamID id)
        {
            SteamNetworking.AcceptP2PSessionWithUser(id);
            Multiplayer.session.pendingSteam.Remove(id);
        }

        private void DrawList<T>(string label, IList<T> entries, Func<T, string> entryString, ref Rect inRect, ref Vector2 scroll, Action<T> click = null, bool hideEmpty = false, string tooltip = null)
        {
            if (hideEmpty && entries.Count == 0) return;

            Widgets.Label(inRect, label);
            inRect.yMin += 20f;

            float entryHeight = Text.LineHeight;
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

                Rect entryRect = new Rect(0, y, viewRect.width, entryHeight);
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

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect, entryLabel);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            GUI.color = Color.white;
            Widgets.EndScrollView();

            inRect.yMin += outRect.height;
        }

        private void DrawChat(Rect inRect)
        {
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

            foreach (ChatMsg msg in messages)
            {
                float height = Text.CalcHeight(msg.Msg, width);
                float textWidth = Text.CalcSize(msg.Msg).x + 15;

                Rect msgRect = new Rect(20f, yPos, width, height);
                if (Mouse.IsOver(msgRect))
                {
                    GUI.DrawTexture(msgRect, SelectedMsg);

                    if (msg.TimeStamp != null)
                        TooltipHandler.TipRegion(msgRect, msg.TimeStamp.ToLongTimeString());

                    if (msg.Clickable && Event.current.type == EventType.MouseUp)
                        msg.Click();
                }

                Color cursorColor = GUI.skin.settings.cursorColor;
                GUI.skin.settings.cursorColor = new Color(0, 0, 0, 0);

                msgRect.width = Math.Min(textWidth, msgRect.width);

                GUI.SetNextControlName("chat_msg_" + i++);
                Widgets.TextArea(msgRect, msg.Msg, true);

                GUI.skin.settings.cursorColor = cursorColor;

                yPos += height;
            }

            if (Event.current.type == EventType.layout)
                messagesHeight = yPos;

            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(textField.xMax + 5f, textField.y, 55f, textField.height), "Send"))
                SendMsg();

            GUI.EndGroup();

            if (Event.current.type == EventType.mouseDown && !GUI.GetNameOfFocusedControl().NullOrEmpty())
                UI.UnfocusCurrentControl();

            if (!hasBeenFocused)
            {
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

            if (Multiplayer.Client == null)
                AddMsg(Multiplayer.username + ": " + currentMsg);
            else
                Multiplayer.Client.Send(Packets.Client_Chat, currentMsg);

            currentMsg = "";
        }

        public void AddMsg(string msg)
        {
            AddMsg(new ChatMsg_Text(msg, DateTime.Now));
        }

        public void AddMsg(ChatMsg msg)
        {
            messages.Add(msg);
            chatScroll.y = messagesHeight;
        }
    }

    public abstract class ChatMsg
    {
        public virtual bool Clickable => false;
        public abstract string Msg { get; }
        public abstract DateTime TimeStamp { get; }

        public virtual void Click() { }
    }

    public class ChatMsg_Text : ChatMsg
    {
        private string msg;
        private DateTime timestamp;

        public override string Msg => msg;
        public override DateTime TimeStamp => timestamp;

        public ChatMsg_Text(string msg, DateTime timestamp)
        {
            this.msg = msg;
            this.timestamp = timestamp;
        }
    }
}
