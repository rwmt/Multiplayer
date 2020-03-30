using System;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class DisconnectedWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        private string reason;
        private string desc;

        public bool returnToServerBrowser;

        public DisconnectedWindow(string reason, string desc = null)
        {
            this.reason = reason;
            this.desc = desc;

            if (reason.NullOrEmpty())
                reason = "Disconnected";

            closeOnAccept = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public const float ButtonHeight = 40f;

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.MiddleCenter;
            Rect labelRect = inRect;
            labelRect.yMax -= ButtonHeight;
            Widgets.Label(labelRect, desc.NullOrEmpty() ? reason : $"<b>{reason}</b>\n{desc}");
            Text.Anchor = TextAnchor.UpperLeft;

            DrawButtons(inRect);
        }

        public virtual void DrawButtons(Rect inRect)
        {
            var buttonWidth = Current.ProgramState == ProgramState.Entry ? 120f : 140f;
            var buttonRect = new Rect((inRect.width - buttonWidth) / 2f, inRect.height - ButtonHeight - 10f, buttonWidth, ButtonHeight);
            var buttonText = Current.ProgramState == ProgramState.Entry ? "CloseButton" : "QuitToMainMenu";

            if (Widgets.ButtonText(buttonRect, buttonText.Translate()))
            {
                if (Current.ProgramState == ProgramState.Entry)
                    Close();
                else
                    GenScene.GoToMainMenu();
            }
        }

        public override void PostClose()
        {
            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }
    }

    public class DefMismatchWindow : DisconnectedWindow
    {
        public override Vector2 InitialSize => new Vector2(460f + 18 * 2, 160f);

        private SessionModInfo mods;

        public DefMismatchWindow(SessionModInfo mods) : base("MpWrongDefs".Translate(), "MpWrongDefsInfo".Translate())
        {
            this.mods = mods;
            returnToServerBrowser = true;
        }

        public override void DrawButtons(Rect inRect)
        {
            var btnWidth = 90f;
            var gap = 10f;

            Rect btnRect = new Rect(gap, inRect.height - ButtonHeight - 10f, btnWidth, ButtonHeight);

            if (Widgets.ButtonText(btnRect, "Details".Translate()))
            {
                var defs = mods.defInfo.Where(kv => kv.Value.status != DefCheckStatus.OK).Join(kv => $"{kv.Key}: {kv.Value.status}", delimiter: "\n");

                Find.WindowStack.Add(new TextAreaWindow($"Mismatches:\n\n{defs}"));
            }

            btnRect.x += btnWidth + gap;

            if (Widgets.ButtonText(btnRect, "MpModList".Translate()))
                ShowModList(mods);
            btnRect.x += btnWidth + gap;

            btnRect.width = 140f;
            if (Widgets.ButtonText(btnRect, "MpSyncModList".Translate())) {
                Log.Message("MP remote host's modIds: " + string.Join(", ", mods.remoteModIds));
                Log.Message("MP remote host's workshopIds: " + string.Join(", ", mods.remoteWorkshopModIds));

                LongEventHandler.QueueLongEvent(() => {
                    ModManagement.DownloadWorkshopMods(mods.remoteWorkshopModIds);
                    try {
                        ModManagement.RebuildModsList();
                        ModsConfig.SetActiveToList(mods.remoteModIds.ToList());
                        ModsConfig.Save();
                        ModsConfig.RestartFromChangedMods();
                    }
                    catch (Exception e) {
                        Log.Error($"MP mod sync error: {e.GetType()} {e.Message}");
                    }
                }, "MPDownloadingWorkshopMods", true, null);
            }

            btnRect.x += btnRect.width + gap;
            btnRect.width = btnWidth;

            if (Widgets.ButtonText(btnRect, "CloseButton".Translate()))
                Close();
        }

        public static void ShowModList(SessionModInfo mods)
        {
            var activeMods = LoadedModManager.RunningModsListForReading.Join(m => "+ " + m.Name, "\n");
            var serverMods = mods.remoteModNames.Join(name => (ModLister.AllInstalledMods.Any(m => m.Name == name) ? "+ " : "- ") + name, delimiter: "\n");

            Find.WindowStack.Add(new TwoTextAreas_Window($"RimWorld {mods.remoteRwVersion}\nServer mod list:\n\n{serverMods}", $"RimWorld {VersionControl.CurrentVersionString}\nActive mod list:\n\n{activeMods}"));
        }
    }

}
