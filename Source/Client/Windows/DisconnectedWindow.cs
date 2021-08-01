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

        protected string reason;
        protected string desc;

        public bool returnToServerBrowser;

        public DisconnectedWindow(string reason, string desc = null)
        {
            this.reason = reason;
            this.desc = desc;

            if (reason.NullOrEmpty())
                this.reason = "MpDisconnected".Translate();

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
        public override Vector2 InitialSize => new Vector2(310f + 18 * 2, 160f);

        private RemoteData mods;

        public DefMismatchWindow(RemoteData mods) : base("MpWrongDefs".Translate(), "MpWrongDefsInfo".Translate())
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

            if (Widgets.ButtonText(btnRect, "CloseButton".Translate()))
                Close();
        }

        public static void ShowModList(RemoteData mods)
        {
            var activeMods = LoadedModManager.RunningModsListForReading.Join(m => "+ " + m.Name, "\n");
            var serverMods = mods.remoteModNames.Join(name => (ModLister.AllInstalledMods.Any(m => m.Name == name) ? "+ " : "- ") + name, delimiter: "\n");

            Find.WindowStack.Add(new TwoTextAreas_Window($"RimWorld {mods.remoteRwVersion}\nServer mod list:\n\n{serverMods}", $"RimWorld {VersionControl.CurrentVersionString}\nActive mod list:\n\n{activeMods}"));
        }
    }

    public class ModsMismatchWindow : DisconnectedWindow
    {
        private RemoteData mods;
        private bool modsMatch;
        private bool modConfigsMatch;
        private Action continueConnecting;
        private bool shouldCloseConnection = true;

        public ModsMismatchWindow(RemoteData mods, Action continueConnecting)
            : base("MpWrongDefs".Translate(), "MpWrongDefsInfo".Translate())
        {
            this.mods = mods;
            this.continueConnecting = continueConnecting;
            returnToServerBrowser = true;
            modsMatch = ModManagement.ModsMatch(mods.remoteModIds);
            modConfigsMatch = ModManagement.CheckModConfigsMatch(mods.remoteModConfigs);
            if (modsMatch) {
                reason = "MpWrongModConfigs".Translate();
                desc = "MpWrongModConfigsInfo".Translate();
            }
        }

        public override Vector2 InitialSize {
            get {
                float buttonsWidth;
                if (!modsMatch) {
                    buttonsWidth = !modConfigsMatch ? 460f : 290f;
                }
                else {
                    buttonsWidth = 460f;
                }
                return new Vector2(buttonsWidth + 20 + 18 * 2, 160f);
            }
        }

        public override void DrawButtons(Rect inRect)
        {
            var btnWidth = 90f;
            var gap = 10f;

            Rect btnRect = new Rect(gap, inRect.height - ButtonHeight - 10f, btnWidth, ButtonHeight);

            if (!modsMatch) {
                if (Widgets.ButtonText(btnRect, "MpModList".Translate())) {
                    ShowModList(mods);
                }
                btnRect.x += btnWidth + gap;

                if (!modConfigsMatch) {
                    btnRect.width = 160f;
                    if (Widgets.ButtonText(btnRect, "MpSyncModsAndConfigs".Translate())) {
                        SyncModsAndConfigs(true);
                    }
                    btnRect.x += btnRect.width + gap;
                    btnRect.width = btnWidth;
                }

                if (Widgets.ButtonText(btnRect, "MpSyncMods".Translate())) {
                    SyncModsAndConfigs(false);
                }
                btnRect.x += btnWidth + gap;
            }
            else {
                if (Widgets.ButtonText(btnRect, "MpConnectButton".Translate())) {
                    shouldCloseConnection = false;
                    returnToServerBrowser = false;
                    Close();
                    continueConnecting();
                }
                btnRect.x += btnWidth + gap;

                if (Widgets.ButtonText(btnRect, "Details".Translate())) {
                    ShowConfigsList(mods);
                }
                btnRect.x += btnWidth + gap;

                btnRect.width = 160f;
                if (Widgets.ButtonText(btnRect, "MpSyncModConfigs".Translate())) {
                    SyncModsAndConfigs(true);
                }
                btnRect.x += btnRect.width + gap;
                btnRect.width = btnWidth;
            }

            if (Widgets.ButtonText(btnRect, "CloseButton".Translate())) {
                Close();
            }
        }

        private void SyncModsAndConfigs(bool syncConfigs) {
            Log.Message("MP remote host's modIds: " + string.Join(", ", mods.remoteModIds));
            Log.Message("MP remote host's workshopIds: " + string.Join(", ", mods.remoteWorkshopModIds));

            LongEventHandler.QueueLongEvent(() => {
                try {
                    ModManagement.DownloadWorkshopMods(mods.remoteWorkshopModIds);
                }
                catch (InvalidOperationException e) {
                    Log.Warning($"MP Workshop mod download error: {e.Message}");
                    var missingMods = ModManagement.GetNotInstalledMods(mods.remoteModIds).ToList();
                    if (missingMods.Any()) {
                        Find.WindowStack.Add(new DebugTextWindow(
                            $"Failed to connect to Workshop.\nThe following mods are missing, please install them:\n"
                            + missingMods.Join(s => $"- {s}", "\n")
                        ));
                        return;
                    }
                }

                try {
                    ModManagement.RebuildModsList();
                    ModsConfig.SetActiveToList(mods.remoteModIds.ToList());
                    ModsConfig.Save();
                    if (syncConfigs) {
                        ModManagement.ApplyHostModConfigFiles(mods.remoteModConfigs);
                    }
                    ModManagement.PromptRestartAndReconnect(mods.remoteAddress, mods.remotePort);
                }
                catch (Exception e) {
                    Log.Error($"MP mod sync error: {e.GetType()} {e.Message}");
                    Find.WindowStack.Add(new DebugTextWindow($"Failed to sync mods: {e.GetType()} {e.Message}"));
                }
            }, "MpDownloadingWorkshopMods", true, null);
        }

        private static void ShowModList(RemoteData mods)
        {
            var activeMods = LoadedModManager.RunningModsListForReading.Join(m => "+ " + m.Name, "\n");
            var serverMods = mods.remoteModNames.Join(name => (ModLister.AllInstalledMods.Any(m => m.Name == name) ? "+ " : "- ") + name, delimiter: "\n");

            Find.WindowStack.Add(new TwoTextAreas_Window($"RimWorld {mods.remoteRwVersion}\nServer mod list:\n\n{serverMods}", $"RimWorld {VersionControl.CurrentVersionString}\nActive mod list:\n\n{activeMods}"));
        }

        private static void ShowConfigsList(RemoteData mods)
        {
            var mismatchedModConfigs = ModManagement.GetMismatchedModConfigs(mods.remoteModConfigs);

            Find.WindowStack.Add(new DebugTextWindow($"Mismatched mod configs:\n\n{mismatchedModConfigs.Join(file => "+ " + file, "\n")}"));
        }

        public override void PostClose()
        {
            base.PostClose();
            if (shouldCloseConnection) {
                OnMainThread.StopMultiplayer();
            }
        }
    }
}
