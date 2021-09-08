using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{

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
        public override Vector2 InitialSize => new Vector2(350f, 205f);
        private bool fileExists;
        private bool fullSave = true; // triggers an Autosave

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
                if (Multiplayer.LocalServer != null && fullSave) {
                    Multiplayer.LocalServer.DoAutosave(curName);
                } else {
                    new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{curName}.zip")).Delete();
                    Replay.ForSaving(curName).WriteCurrentData();
                }

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

            fileExists = new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{str}.zip")).Exists;
            return true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            base.DoWindowContents(inRect);

            var entry = new Rect(0f, 95f, 120f, 30f);
            if (Multiplayer.LocalServer != null) {
                TooltipHandler.TipRegion(entry, "MpFullSaveDesc".Translate());
                MpUI.CheckboxLabeled(entry, "MpFullSave".Translate(), ref fullSave, placeTextNearCheckbox: true);
            }
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
        private Vector2 _initialSize;
        public override Vector2 InitialSize => _initialSize;

        private Vector2 scroll;
        private string text;
        private List<string> lines;

        private float fullHeight;

        public DebugTextWindow(string text, float width=800, float height=450)
        {
            this.text = text;
            this._initialSize = new Vector2(width, height);
            absorbInputAroundWindow = false;
            doCloseX = true;
            draggable = true;

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
        public override Vector2 InitialSize => new Vector2(600, 500);

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
