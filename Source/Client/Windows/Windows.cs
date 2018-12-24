using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class PacketLogWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);

        public List<LogNode> nodes = new List<LogNode>();
        private int logHeight;
        private Vector2 scrollPos;

        public PacketLogWindow()
        {
            doCloseX = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            GUI.BeginGroup(rect);

            Text.Font = GameFont.Tiny;
            Rect outRect = new Rect(0f, 0f, rect.width, rect.height - 30f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, logHeight + 10f);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            Rect nodeRect = new Rect(0f, 0f, viewRect.width, 20f);
            foreach (LogNode node in nodes)
                Draw(node, 0, ref nodeRect);

            if (Event.current.type == EventType.layout)
                logHeight = (int)nodeRect.y;

            Widgets.EndScrollView();

            GUI.EndGroup();
        }

        public void Draw(LogNode node, int depth, ref Rect rect)
        {
            string text = node.text;
            if (depth == 0)
                text = node.children[0].text;

            rect.x = depth * 15;
            if (node.children.Count > 0)
            {
                Widgets.Label(rect, node.expand ? "[-]" : "[+]");
                rect.x += 15;
            }

            rect.height = Text.CalcHeight(text, rect.width);
            Widgets.Label(rect, text);
            if (Widgets.ButtonInvisible(rect))
                node.expand = !node.expand;
            rect.y += (int)rect.height;

            if (node.expand)
                foreach (LogNode child in node.children)
                    Draw(child, depth + 1, ref rect);
        }
    }

    public class DesyncedWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(550, 110);

        private string text;

        public DesyncedWindow(string text)
        {
            this.text = text;

            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(0, 0, inRect.width, 40), $"{"MpDesynced".Translate()}\n{text}");
            Text.Anchor = TextAnchor.UpperLeft;

            float buttonWidth = 120 * 4 + 10 * 3;
            var buttonRect = new Rect((inRect.width - buttonWidth) / 2, 40, buttonWidth, 35);

            GUI.BeginGroup(buttonRect);

            float x = 0;
            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpTryResync".Translate()))
            {
                TickPatch.skipToTickUntil = true;
                TickPatch.skipTo = 0;
                TickPatch.afterSkip = () => Multiplayer.Client.Send(Packets.Client_WorldReady);
                Multiplayer.session.desynced = false;

                ClientJoiningState.ReloadGame(OnMainThread.cachedMapData.Keys.ToList(), false);
            }
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Save".Translate()))
                Find.WindowStack.Add(new Dialog_SaveReplay());
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpChatButton".Translate()))
                Find.WindowStack.Add(new ChatWindow() { closeOnClickedOutside = true, absorbInputAroundWindow = true });
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Quit".Translate()))
            {
                OnMainThread.StopMultiplayer();
                GenScene.GoToMainMenu();
            }

            GUI.EndGroup();
        }
    }

    public class Dialog_SaveReplay : Window
    {
        public override Vector2 InitialSize => new Vector2(350f, 175f);

        private string curName;
        private bool fileExists;
        private bool focused;

        public Dialog_SaveReplay()
        {
            closeOnClickedOutside = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = true;

            curName = Multiplayer.session.gameName;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0, 15f, inRect.width, 35f), "MpSaveReplayAs".Translate());

            GUI.SetNextControlName("RenameField");
            string text = Widgets.TextField(new Rect(0, 25 + 15f, inRect.width, 35f), curName);
            if (curName != text && text.Length < 30 || !focused)
            {
                curName = text;
                fileExists = new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{curName}.zip")).Exists;
            }

            if (fileExists)
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0, 25 + 15 + 35, inRect.width, 35f), "MpWillOverwrite".Translate());
                Text.Font = GameFont.Small;
            }

            if (!focused)
            {
                UI.FocusControl("RenameField", this);
                focused = true;
            }

            if (Widgets.ButtonText(new Rect(0f, inRect.height - 35f - 5f, 120f, 35f).CenteredOnXIn(inRect), "OK".Translate(), true, false, true))
                TrySave();
        }

        public override void OnAcceptKeyPressed()
        {
            if (TrySave())
                base.OnAcceptKeyPressed();
        }

        private bool TrySave()
        {
            if (curName.Length == 0) return false;

            try
            {
                new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{curName}.zip")).Delete();
                Replay.ForSaving(curName).WriteCurrentData();
                Close();
                Messages.Message("MpReplaySaved".Translate(), MessageTypeDefOf.SilentInput, false);
            }
            catch (Exception e)
            {
                Log.Error($"Exception saving replay {e}");
            }

            return true;
        }
    }

    public class DebugTextWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(600, 300);

        private Vector2 scroll;
        private string text;

        public DebugTextWindow(string text)
        {
            absorbInputAroundWindow = true;
            doCloseX = true;

            this.text = text;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.TextAreaScrollable(inRect, text, ref scroll);
        }
    }

}
