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
        public const int MaxChatMsgLength = 128;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);

        private static readonly Texture2D SelectedMsg = SolidColorMaterials.NewSolidColorTexture(new Color(0.17f, 0.17f, 0.17f, 0.85f));

        private Vector2 chatScroll;
        private Vector2 playerListScroll;
        private Vector2 steamScroll;
        private float messagesHeight;
        private List<ChatMsg> messages = new List<ChatMsg>();
        private string currentMsg = "";
        private bool focused;

        public string[] playerList = new string[0];

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

            DrawList($"Players ({playerList.Length}):", playerList, ref inRect, ref playerListScroll, index =>
            {
                if (Multiplayer.LocalServer != null && Event.current.button == 1)
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>()
                    {
                        new FloatMenuOption("Kick", () =>
                        {
                            // todo
                        })
                    }));
            });

            inRect.yMin += 10f;

            List<string> names = Multiplayer.session.pendingSteam.Select(SteamFriends.GetFriendPersonaName).ToList();
            DrawList("Accept Steam players", names, ref inRect, ref steamScroll, AcceptSteamPlayer, true);
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
                Widgets.CheckboxLabeled(optionsRect, label, ref Multiplayer.LocalServer.allowLan);
                inRect.yMax -= 20;
            }
        }

        private void AcceptSteamPlayer(int index)
        {
            CSteamID remoteId = Multiplayer.session.pendingSteam[index];

            IConnection conn = new SteamConnection(remoteId);
            conn.State = new ServerSteamState(conn);
            Multiplayer.LocalServer.OnConnected(conn);

            SteamNetworking.AcceptP2PSessionWithUser(remoteId);
            Multiplayer.session.pendingSteam.RemoveAt(index);
        }

        private void DrawList(string label, IList<string> entries, ref Rect inRect, ref Vector2 scroll, Action<int> click = null, bool hideNullOrEmpty = false)
        {
            if (hideNullOrEmpty && !entries.Any(s => !s.NullOrEmpty())) return;

            Widgets.Label(inRect, label);
            inRect.yMin += 20f;

            float entryHeight = Text.LineHeight;
            float height = entries.Count() * entryHeight;

            Rect outRect = new Rect(0, inRect.yMin, inRect.width, Math.Min(height, Math.Min(230, inRect.height)));
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, height);
            if (viewRect.height <= outRect.height)
                viewRect.width += 16f;

            Widgets.BeginScrollView(outRect, ref scroll, viewRect, true);
            GUI.color = new Color(1, 1, 1, 0.8f);

            float y = height - entryHeight;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                string entry = entries[i];
                if (hideNullOrEmpty && entry.NullOrEmpty()) continue;

                Rect entryRect = new Rect(0, y, viewRect.width, entryHeight);
                if (i % 2 == 0)
                    Widgets.DrawAltRect(entryRect);

                if (Mouse.IsOver(entryRect))
                {
                    GUI.DrawTexture(entryRect, SelectedMsg);
                    if (click != null && Event.current.type == EventType.mouseDown)
                    {
                        click(i);
                        Event.current.Use();
                    }
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect, entry);
                Text.Anchor = TextAnchor.UpperLeft;

                y -= entryHeight;
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
            currentMsg = currentMsg.Substring(0, Math.Min(currentMsg.Length, MaxChatMsgLength));

            Widgets.BeginScrollView(outRect, ref chatScroll, viewRect);

            float yPos = 0;
            GUI.color = Color.white;

            int i = 0;

            foreach (ChatMsg msg in messages)
            {
                float height = Text.CalcHeight(msg.Msg, width);
                float textWidth = Text.CalcSize(msg.Msg).x + 15;

                GUI.SetNextControlName("chat_msg_" + i++);

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

            if (!focused)
            {
                GUI.FocusControl("chat_input");
                TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                editor.OnFocus();
                editor.MoveTextEnd();
                focused = true;
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            focused = false;
        }

        public void SendMsg()
        {
            currentMsg = currentMsg.Trim();

            if (currentMsg.NullOrEmpty()) return;

            if (Multiplayer.Client == null)
                AddMsg(Multiplayer.username + ": " + currentMsg);
            else
                Multiplayer.Client.Send(Packets.CLIENT_CHAT, currentMsg);

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
