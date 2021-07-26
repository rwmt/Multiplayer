using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
    public static class MainMenuMarker
    {
        public static bool drawing;

        static void Prefix() => drawing = true;
        static void Postfix() => drawing = false;
    }

    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
    public static class MainMenu_AddHeight
    {
        static void Prefix(ref Rect rect) => rect.height += 45f;
    }

    [HotSwappable]
    [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
    public static class MainMenuPatch
    {
        static void Prefix(Rect rect, List<ListableOption> optList)
        {
            if (!MainMenuMarker.drawing) return;

            if (Current.ProgramState == ProgramState.Entry)
            {
                int newColony = optList.FindIndex(opt => opt.label == "NewColony".Translate());
                if (newColony != -1)
                {
                    optList.Insert(newColony + 1, new ListableOption("MpMultiplayer".Translate(), () =>
                    {
                        if (Prefs.DevMode && Event.current.button == 1)
                            ShowModDebugInfo();
                        else
                            Find.WindowStack.Add(new ServerBrowser());
                    }));
                }

                int optionsIndex = optList.FindIndex(opt => opt.label == "Options".Translate());
                if (optionsIndex != -1 && ModManagement.HasRecentConfigBackup())
                {
                    var option = new ListableOption("MpRestoreLastConfigs".Translate(), () => {
                        ModManagement.RestoreConfigBackup(ModManagement.GetMostRecentConfigBackup());
                        ModManagement.PromptRestart();
                    });
                    option.minHeight = 30f;
                    optList.Insert(optionsIndex + 1, option);
                }
            }

            if (optList.Any(opt => opt.label == "ReviewScenario".Translate()))
            {
                if (Multiplayer.session == null)
                    optList.Insert(0, new ListableOption(
                        "MpHostServer".Translate(),
                        () => Find.WindowStack.Add(new HostWindow() { layer = WindowLayer.Super })
                    ));

                if (MpVersion.IsDebug && Multiplayer.IsReplay)
                    optList.Insert(0, new ListableOption(
                        "MpHostServer".Translate(),
                        () => Find.WindowStack.Add(new HostWindow(withSimulation: true) { layer = WindowLayer.Super })
                    ));

                if (Multiplayer.Client != null)
                {
                    optList.RemoveAll(opt => opt.label == "Save".Translate() || opt.label == "LoadGame".Translate());
                    if (!Multiplayer.IsReplay)
                    {
                        optList.Insert(0, new ListableOption("Save".Translate(), () => Find.WindowStack.Add(new Dialog_SaveReplay())));
                    }
                    optList.Insert(3, new ListableOption("MpConvert".Translate(), ConvertToSingleplayer));

                    var quitMenuLabel = "QuitToMainMenu".Translate();
                    var saveAndQuitMenu = "SaveAndQuitToMainMenu".Translate();
                    var quitMenu = optList.Find(opt => opt.label == quitMenuLabel || opt.label == saveAndQuitMenu);

                    if (quitMenu != null)
                    {
                        quitMenu.label = quitMenuLabel;
                        quitMenu.action = AskQuitToMainMenu;
                    }

                    var quitOSLabel = "QuitToOS".Translate();
                    var saveAndQuitOSLabel = "SaveAndQuitToOS".Translate();
                    var quitOS = optList.Find(opt => opt.label == quitOSLabel || opt.label == saveAndQuitOSLabel);

                    if (quitOS != null)
                    {
                        quitOS.label = quitOSLabel;
                        quitOS.action = () =>
                        {
                            if (Multiplayer.LocalServer != null)
                                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("MpServerCloseConfirmation".Translate(), Root.Shutdown, true));
                            else
                                Root.Shutdown();
                        };
                    }
                }
            }
        }

        static void ShowModDebugInfo()
        {
            var info = new RemoteData();
            JoinData.ReadServerData(JoinData.WriteServerData(), info);
            info.remoteFiles.Add("rwmt.multiplayer", new ModFile() { relPath = "/Test/Test.xml" });

            Find.WindowStack.Add(new JoinDataWindow(info));
        }

        public static void AskQuitToMainMenu()
        {
            if (Multiplayer.LocalServer != null)
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("MpServerCloseConfirmation".Translate(), GenScene.GoToMainMenu, true));
            else
                GenScene.GoToMainMenu();
        }

        private static void ConvertToSingleplayer()
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                var saveName = Multiplayer.session.gameName + "-preconvert";
                new FileInfo(Path.Combine(Multiplayer.ReplaysDir, $"{saveName}.zip")).Delete();
                Replay.ForSaving(saveName).WriteCurrentData();

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
            }, "Play", "MpConverting", true, null);
        }
    }

    [HarmonyPatch]
    static class Shutdown_Quit_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GenScene), nameof(GenScene.GoToMainMenu));
            yield return AccessTools.Method(typeof(Root), nameof(Root.Shutdown));
        }

        static void Prefix()
        {
            OnMainThread.StopMultiplayer();
        }
    }
}
