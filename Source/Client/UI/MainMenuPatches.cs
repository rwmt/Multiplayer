using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RestSharp;
using UnityEngine;
using UnityEngine.Diagnostics;
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
                    optList.Insert(newColony + 1, new ListableOptionWithMarker("MpMultiplayerButton".Translate(), () =>
                    {
                        if (MpVersion.IsDebug && Event.current.button == 1)
                            ShowModDebugInfo();
                        else
                            Find.WindowStack.Add(new ServerBrowser());
                    }));
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
                        optList.Insert(0, new ListableOption("Save".Translate(), () => Find.WindowStack.Add(new SaveGameWindow(Multiplayer.session.gameName) { layer = WindowLayer.Super })));
                    }

                    var quitMenuLabel = "QuitToMainMenu".Translate();
                    var saveAndQuitMenu = "SaveAndQuitToMainMenu".Translate();
                    int? quitToMenuIndex = optList.IndexNullable(opt => opt.label == quitMenuLabel || opt.label == saveAndQuitMenu);

                    if (quitToMenuIndex is { } i1)
                    {
                        optList[i1].label = quitMenuLabel;
                        optList[i1].action = AskQuitToMainMenu;
                    }

                    var quitOSLabel = "QuitToOS".Translate();
                    var saveAndQuitOSLabel = "SaveAndQuitToOS".Translate();
                    var quitOSOptIndex = optList.IndexNullable(opt => opt.label == quitOSLabel || opt.label == saveAndQuitOSLabel);

                    if (quitOSOptIndex is { } i2)
                    {
                        optList[i2].label = quitOSLabel;
                        optList[i2].action = () =>
                        {
                            if (Multiplayer.LocalServer != null)
                                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(GetServerCloseConfirmation(), Root.Shutdown, true, layer: WindowLayer.Super));
                            else
                                Root.Shutdown();
                        };
                    }

                    optList.Insert(
                        quitToMenuIndex ?? quitOSOptIndex ?? 0,
                        new ListableOption("MpConvertToSp".Translate(), AskConvertToSingleplayer)
                    );
                }
            }
        }

        static void ShowModDebugInfo()
        {
            Find.WindowStack.Add(new DisconnectedWindow(new SessionDisconnectInfo() { specialButtonTranslated = "Special btn"}));
            return;

            var info = new RemoteData();
            JoinData.ReadServerData(JoinData.WriteServerData(true), info);
            for (int i = 0; i < 200; i++)
                info.remoteMods.Add(info.remoteMods.Last());
            info.remoteFiles.Add("rwmt.multiplayer", new ModFile() { relPath = "/Test/Test.xml" });
            //info.remoteFiles.Add("ludeon.rimworld", new ModFile() { relPath = "/Test/Test.xml" });

            Find.WindowStack.Add(new JoinDataWindow(info));
        }

        public static void AskQuitToMainMenu()
        {
            if (Multiplayer.LocalServer == null)
            {
                GenScene.GoToMainMenu();
                return;
            }

            Find.WindowStack.Add(
                Dialog_MessageBox.CreateConfirmation(
                    GetServerCloseConfirmation(),
                    GenScene.GoToMainMenu,
                    true,
                    layer: WindowLayer.Super
                )
            );
        }

        static string GetServerCloseConfirmation()
        {
            float? seconds = Time.realtimeSinceStartup - Multiplayer.session.lastSaveAt;
            if (seconds is null or < 10)
                return "MpServerCloseConfirmationNoTime".Translate();

            var minutes = seconds / 60;
            return "MpServerCloseConfirmationTime".Translate(minutes > 0 ? $"{minutes:0.00}min" : $"{seconds:0.00}s");
        }

        private static void AskConvertToSingleplayer()
        {
            static void Convert()
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    try
                    {
                        const string suffix = "-preconvert";
                        var saveName = $"{GenFile.SanitizedFileName(Multiplayer.session.gameName)}{suffix}";

                        new FileInfo(Path.Combine(Multiplayer.ReplaysDir, saveName + ".zip")).Delete();
                        Replay.ForSaving(saveName).WriteCurrentData();
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Convert to singleplayer failed to write pre-convert file: {e}");
                    }

                    Find.GameInfo.permadeathMode = false;
                    HostUtil.SetAllUniqueIds(Multiplayer.GlobalIdBlock.Current);

                    Multiplayer.StopMultiplayer();

                    var doc = SaveLoad.SaveGameToDoc();
                    MemoryUtility.ClearAllMapsAndWorld();

                    Current.Game = new Game();
                    Current.Game.InitData = new GameInitData();
                    Current.Game.InitData.gameToLoad = "play";

                    LoadPatch.gameToLoad = new TempGameData(doc, new byte[0]);
                }, "Play", "MpConvertingToSp", true, null);
            }

            Find.WindowStack.Add(
                Dialog_MessageBox.CreateConfirmation(
                    Multiplayer.LocalServer != null ? "MpConvertToSpWarnHost".Translate() : "MpConvertToSpWarn".Translate(),
                    Convert,
                    true,
                    layer: WindowLayer.Super
                )
            );
        }
    }

    class ListableOptionWithMarker : ListableOption
    {
        public ListableOptionWithMarker(string label, Action action, string uiHighlightTag = null) : base(label, action, uiHighlightTag)
        {
        }

        public override float DrawOption(Vector2 pos, float width)
        {
            var r = base.DrawOption(pos, width);

            if (Multiplayer.loadingErrors)
            {
                float b = Text.CalcHeight(label, width);
                float num = Mathf.Max(minHeight, b);
                Rect rect = new Rect(pos.x, pos.y, width, num);
                var markerRect = new Rect(rect.xMax - 36, rect.center.y - 12, 24, 24);
                GUI.DrawTexture(markerRect, Widgets.CheckboxOffTex);
                TooltipHandler.TipRegion(markerRect, MpUtil.TranslateWithDoubleNewLines("MpLoadingError", 5));
            }

            return r;
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
            Multiplayer.StopMultiplayer();
        }
    }
}
