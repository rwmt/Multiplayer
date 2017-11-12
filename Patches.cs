using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace ServerMod
{
    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.DoSingleTick))]
    public static class TickPatch
    {
        static void Postfix()
        {
            // when not paused, wait for synchronized tick
            while (ServerMod.actions.Count > 0 && Find.TickManager.CurTimeSpeed != TimeSpeed.Paused && ServerMod.actions.Peek().ticks == Find.TickManager.TicksGame)
                OnMainThread.ExecuteServerAction(ServerMod.actions.Dequeue());
        }
    }

    [HarmonyPatch(typeof(OptionListingUtility))]
    [HarmonyPatch(nameof(OptionListingUtility.DrawOptionListing))]
    public static class MainMenuPatch
    {
        static void Prefix(Rect rect, List<ListableOption> optList)
        {
            int newColony = optList.FindIndex(opt => opt.label == "NewColony".Translate());
            if (newColony != -1)
                optList.Insert(newColony + 1, new ListableOption("Connect to server", () =>
                {
                    Find.WindowStack.Add(new ConnectWindow());
                }));

            int reviewScenario = optList.FindIndex(opt => opt.label == "ReviewScenario".Translate());
            if (reviewScenario != -1)
                AddHostButton(optList);
        }

        public static void AddHostButton(List<ListableOption> buttons)
        {
            if (ServerMod.server != null)
                buttons.Insert(0, new ListableOption("Server info", () =>
                {
                    Find.WindowStack.Add(new ServerInfoWindow());
                }));
            else if (ServerMod.client == null)
                buttons.Insert(0, new ListableOption("Host a server", () =>
                {
                    Find.WindowStack.Add(new HostWindow());
                }));
        }
    }

    [HarmonyPatch(typeof(SavedGameLoader))]
    [HarmonyPatch(nameof(SavedGameLoader.LoadGameFromSaveFile))]
    [HarmonyPatch(new Type[] { typeof(string) })]
    public static class LoadPatch
    {
        static bool Prefix(string fileName)
        {
            if (ServerMod.savedWorld != null && fileName == "server")
            {
                ScribeUtil.StartLoading(ServerMod.savedWorld);

                if (Scribe.EnterNode("game"))
                {
                    Current.Game = new Game();
                    Current.Game.InitData = new GameInitData();
                    Prefs.PauseOnLoad = false;
                    Current.Game.LoadGame(); // this calls Scribe.loader.FinalizeLoading()
                    Prefs.PauseOnLoad = true;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                }

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    ServerMod.client.State = new ClientPlayingState(ServerMod.client);
                    ServerMod.client.Send(Packets.CLIENT_WORLD_FINISHED);

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        ServerMod.pause.WaitOne();
                        OnMainThread.Queue(() => FinishLoading());
                    }, "Waiting for other players to load", true, null);
                });

                return false;
            }

            return true;
        }

        private static void FinishLoading()
        {
            Faction.OfPlayer.def = FactionDefOf.Outlander;
            Faction clientFaction = Find.World.GetComponent<PlayerFactions>().playerFactions[ServerMod.username];
            clientFaction.def = FactionDefOf.PlayerColony;
            Find.GameInitData.playerFaction = clientFaction;
            Log.Message("Client faction: " + clientFaction.Name + " / " + clientFaction.GetUniqueLoadID());

            CustomSelectLandingSite page = new CustomSelectLandingSite();
            page.nextAct = () =>
            {
                Find.GameInitData.mapSize = 150;
                Find.GameInitData.startingPawns.Add(StartingPawnUtility.NewGeneratedStartingPawn());
                Find.GameInitData.PrepForMapGen();
                Find.Scenario.PreMapGenerate(); // this creates the FactionBase WorldObject
                IntVec3 intVec = new IntVec3(Find.GameInitData.mapSize, 1, Find.GameInitData.mapSize);
                FactionBase factionBase = Find.WorldObjects.FactionBases.First(faction => faction.Faction == Faction.OfPlayer);
                Map visibleMap = MapGenerator.GenerateMap(intVec, factionBase, factionBase.MapGeneratorDef, factionBase.ExtraGenStepDefs, null);
                Find.World.info.initialMapSize = intVec;
                PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.NewColonyOptimism);
                Current.Game.FinalizeInit();
                Current.Game.VisibleMap = visibleMap;
                Find.CameraDriver.JumpToVisibleMapLoc(MapGenerator.PlayerStartSpot);
                Find.CameraDriver.ResetSize();
                Current.Game.InitData = null;
            };

            Find.WindowStack.Add(page);
            MemoryUtility.UnloadUnusedUnityAssets();
            Find.World.renderer.RegenerateAllLayersNow();
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot))]
    [HarmonyPatch(nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class ButtonsPatch
    {
        static bool Prefix()
        {
            Text.Font = GameFont.Small;
            string text = Find.TickManager.TicksGame.ToString();
            Rect rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text, 330f));
            Widgets.Label(rect, text);

            return !Find.WindowStack.IsOpen<CustomSelectLandingSite>();
        }
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.TickManagerUpdate))]
    public static class TickUpdatePatch
    {
        private static TimeSpeed lastSpeed;

        static bool Prefix()
        {
            if (ServerMod.client != null && Find.TickManager.CurTimeSpeed != lastSpeed)
            {
                ServerMod.client.Send(Packets.CLIENT_ACTION_REQUEST, BitConverter.GetBytes((int)(Find.TickManager.CurTimeSpeed == TimeSpeed.Paused ? ServerAction.PAUSE : ServerAction.UNPAUSE)));
                Log.Message("client request at: " + Find.TickManager.TicksGame);

                Find.TickManager.CurTimeSpeed = lastSpeed;
                return false;
            }

            lastSpeed = Find.TickManager.CurTimeSpeed;
            return true;
        }

        public static void SetSpeed(TimeSpeed speed)
        {
            Find.TickManager.CurTimeSpeed = speed;
            lastSpeed = speed;
        }
    }

    [HarmonyPatch(typeof(WorldObjectsHolder))]
    [HarmonyPatch(nameof(WorldObjectsHolder.Add))]
    public static class WorldObjectsHolderPatch
    {
        static void Postfix(WorldObject o)
        {
            if (!(o is FactionBase))
                return;
            
            ScribeUtil.StartWriting();
            Scribe.EnterNode("data");
            Scribe_Deep.Look(ref o, "worldObj");
            byte[] data = ScribeUtil.FinishWriting();
            ServerMod.client.Send(Packets.CLIENT_NEW_WORLD_OBJ, data);
        }
    }
}
