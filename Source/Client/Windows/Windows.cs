using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Profile;

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

            if (Event.current.type == EventType.Layout)
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
        public override Vector2 InitialSize => new Vector2(680, 110);

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

            float buttonWidth = 120 * 5 + 10 * 4;
            var buttonRect = new Rect((inRect.width - buttonWidth) / 2, 40, buttonWidth, 35);

            GUI.BeginGroup(buttonRect);

            float x = 0;
            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpTryResync".Translate()))
            {
                Multiplayer.session.resyncing = true;

                TickPatch.SkipTo(
                    toTickUntil: true,
                    onFinish: () =>
                    {
                        Multiplayer.session.resyncing = false;
                        Multiplayer.Client.Send(Packets.Client_WorldReady);
                    },
                    cancelButtonKey: "Quit",
                    onCancel: GenScene.GoToMainMenu
                );

                Multiplayer.session.desynced = false;

                ClientJoiningState.ReloadGame(OnMainThread.cachedMapData.Keys.ToList(), false);
            }
            x += 120 + 10;


            //REHOST
            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpTryRehost".Translate()))
            {
                Rehost();
            }
            x += 120 + 10;
            //REHOST


            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Save".Translate()))
                Find.WindowStack.Add(new Dialog_SaveReplay());
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpChatButton".Translate()))
                Find.WindowStack.Add(new ChatWindow() { closeOnClickedOutside = true, absorbInputAroundWindow = true });
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Quit".Translate()))
                MainMenuPatch.AskQuitToMainMenu();

            GUI.EndGroup();
        }

        private static void Rehost()
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                Find.GameInfo.permadeathMode = false;
                // todo handle the other faction def too
                Multiplayer.DummyFaction.def = FactionDefOf.Ancients;

                OnMainThread.StopMultiplayer();

                var doc = SaveLoad.SaveGame();
                MemoryUtility.ClearAllMapsAndWorld();

                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.InitData.gameToLoad = "play";

                LoadPatch.gameToLoad = doc;

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    HostWindow window = new HostWindow(null, true);
                    window.forcePause = true;
                    window.absorbInputAroundWindow = true;
                    Find.WindowStack.Add(window);
                });

            }, "Play", "MpConverting", true, null);
        }
    }

    public abstract class MpTextInput : Window
    {
        public override Vector2 InitialSize => new Vector2(350f, 175f);

        public string curName;
        public string title;
        private bool opened;

        public MpTextInput()
        {
            closeOnClickedOutside = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0, 15f, inRect.width, 35f), title);

            GUI.SetNextControlName("RenameField");
            string text = Widgets.TextField(new Rect(0, 25 + 15f, inRect.width, 35f), curName);
            if ((curName != text || !opened) && Validate(text))
                curName = text;

            DrawExtra(inRect);

            if (!opened)
            {
                UI.FocusControl("RenameField", this);
                opened = true;
            }

            if (Widgets.ButtonText(new Rect(0f, inRect.height - 35f - 5f, 120f, 35f).CenteredOnXIn(inRect), "OK".Translate(), true, false, true))
                Accept();
        }

        public override void OnAcceptKeyPressed()
        {
            if (Accept())
                base.OnAcceptKeyPressed();
        }

        public abstract bool Accept();

        public virtual bool Validate(string str) => true;

        public virtual void DrawExtra(Rect inRect) { }
    }

    public class Dialog_SaveReplay : MpTextInput
    {
        private bool fileExists;

        public Dialog_SaveReplay()
        {
            title = "MpSaveReplayAs".Translate();
            curName = Multiplayer.session.gameName;
        }

        public override bool Accept()
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

        public override bool Validate(string str)
        {
            if (str.Length > 30)
                return false;

            fileExists = new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{curName}.zip")).Exists;
            return true;
        }

        public override void DrawExtra(Rect inRect)
        {
            if (fileExists)
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0, 25 + 15 + 35, inRect.width, 35f), "MpWillOverwrite".Translate());
                Text.Font = GameFont.Small;
            }
        }
    }

    public class Dialog_RenameFile : MpTextInput
    {
        private FileInfo file;
        private Action success;

        public Dialog_RenameFile(FileInfo file, Action success = null)
        {
            title = "Rename file to";

            this.file = file;
            this.success = success;

            curName = Path.GetFileNameWithoutExtension(file.Name);
        }

        public override bool Accept()
        {
            if (curName.Length == 0)
                return false;

            string newPath = Path.Combine(file.Directory.FullName, curName + file.Extension);

            if (newPath == file.FullName)
                return true;

            try
            {
                file.MoveTo(newPath);
                Close();
                success?.Invoke();

                return true;
            }
            catch (IOException e)
            {
                Messages.Message(e is DirectoryNotFoundException ? "Error renaming." : "File already exists.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
        }

        public override bool Validate(string str)
        {
            return str.Length < 30;
        }
    }

    public class TextAreaWindow : Window
    {
        private string text;
        private Vector2 scroll;

        public TextAreaWindow(string text)
        {
            this.text = text;

            absorbInputAroundWindow = true;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.TextAreaScrollable(inRect, text, ref scroll);
        }
    }

    public class DebugTextWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(800, 450);

        private Vector2 scroll;
        private string text;
        private List<string> lines;

        private float fullHeight;

        public DebugTextWindow(string text)
        {
            this.text = text;
            absorbInputAroundWindow = true;
            doCloseX = true;

            lines = text.Split('\n').ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float offsetY = -5f;

            if (Event.current.type == EventType.Layout)
            {
                fullHeight = 0;
                foreach (var str in lines)
                    fullHeight += Text.CalcHeight(str, inRect.width) + offsetY;
            }

            Text.Font = GameFont.Tiny;

            if (Widgets.ButtonText(new Rect(0, 0, 55f, 20f), "Copy all"))
                GUIUtility.systemCopyBuffer = text;

            Text.Font = GameFont.Small;

            var viewRect = new Rect(0f, 0f, inRect.width - 16f, Mathf.Max(fullHeight + 10f, inRect.height));
            inRect.y += 30f;
            Widgets.BeginScrollView(inRect, ref scroll, viewRect, true);

            foreach (var str in lines)
            {
                float h = Text.CalcHeight(str, viewRect.width);
                Widgets.TextArea(new Rect(viewRect.x, viewRect.y, viewRect.width, h), str, true);
                viewRect.y += h + offsetY;
            }

            Widgets.EndScrollView();
        }
    }

    public class TwoTextAreas_Window : Window
    {
        public override Vector2 InitialSize => new Vector2(600, 300);

        private Vector2 scroll1;
        private Vector2 scroll2;

        private string left;
        private string right;

        public TwoTextAreas_Window(string left, string right)
        {
            absorbInputAroundWindow = true;
            doCloseX = true;

            this.left = left;
            this.right = right;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.TextAreaScrollable(inRect.LeftHalf(), left, ref scroll1);
            Widgets.TextAreaScrollable(inRect.RightHalf(), right, ref scroll2);
        }
    }

}
