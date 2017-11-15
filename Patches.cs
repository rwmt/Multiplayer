using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

            if (ServerMod.client != null && ServerMod.server == null)
                optList.RemoveAll(opt => opt.label == "Save".Translate());
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

                if (ServerMod.mapsData.Length > 0)
                {
                    XmlDocument mapsXml = new XmlDocument();
                    using (MemoryStream stream = new MemoryStream(ServerMod.mapsData))
                        mapsXml.Load(stream);

                    XmlNode gameNode = Scribe.loader.curXmlParent["game"];
                    gameNode.RemoveChildIfPresent("maps");
                    gameNode["taleManager"]["tales"].RemoveAll();

                    XmlNode newMaps = gameNode.OwnerDocument.ImportNode(mapsXml.DocumentElement["maps"], true);
                    gameNode.AppendChild(newMaps);
                }

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
                    Log.Message("Client maps: " + Current.Game.Maps.Count());

                    ServerMod.savedWorld = null;
                    ServerMod.mapsData = null;

                    ServerMod.client.State = new ClientPlayingState(ServerMod.client);
                    ServerMod.client.Send(Packets.CLIENT_WORLD_FINISHED);

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        ServerMod.pause.WaitOne();

                        if (!Current.Game.Maps.Any())
                            OnMainThread.Queue(() => FinishLoading());
                    }, "Waiting for other players to load", true, null);
                });

                return false;
            }

            return true;
        }

        private static void FinishLoading()
        {
            CustomSelectLandingSite page = new CustomSelectLandingSite();
            page.nextAct = () =>
            {
                Find.GameInitData.mapSize = 150;
                Find.GameInitData.startingPawns.Add(StartingPawnUtility.NewGeneratedStartingPawn());
                Find.GameInitData.startingPawns.Add(StartingPawnUtility.NewGeneratedStartingPawn());
                Find.GameInitData.PrepForMapGen();
                Find.Scenario.PreMapGenerate(); // this creates the FactionBase WorldObject
                IntVec3 intVec = new IntVec3(Find.GameInitData.mapSize, 1, Find.GameInitData.mapSize);
                FactionBase factionBase = Find.WorldObjects.FactionBases.First(faction => faction.Faction == Faction.OfPlayer);
                Map visibleMap = MapGenerator.GenerateMap(intVec, factionBase, factionBase.MapGeneratorDef, factionBase.ExtraGenStepDefs, null);
                Find.World.info.initialMapSize = intVec;
                PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.NewColonyOptimism);
                Current.Game.VisibleMap = visibleMap;
                Find.CameraDriver.JumpToVisibleMapLoc(MapGenerator.PlayerStartSpot);
                Find.CameraDriver.ResetSize();
                Current.Game.InitData = null;

                ClientPlayingState.SyncWorldObj(factionBase);
            };

            Find.WindowStack.Add(page);
            MemoryUtility.UnloadUnusedUnityAssets();
            Find.World.renderer.RegenerateAllLayersNow();
        }
    }

    [HarmonyPatch(typeof(MainButtonsRoot))]
    [HarmonyPatch(nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class MainButtonsPatch
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

    [HarmonyPatch(typeof(GameDataSaveLoader))]
    [HarmonyPatch(nameof(GameDataSaveLoader.SaveGame))]
    public static class SavePatch
    {
        static bool Prefix()
        {
            if (ServerMod.client == null || ServerMod.server != null)
                return true;

            ScribeUtil.StartWriting();
            Scribe.EnterNode("savedMaps");
            List<Map> list = Current.Game.Maps.FindAll(map => map.IsPlayerHome);
            Scribe_Collections.Look(ref list, "maps", LookMode.Deep);
            byte[] data = ScribeUtil.FinishWriting();
            ServerMod.client.Send(Packets.CLIENT_SAVE_MAP, data);

            return false;
        }
    }

    [HarmonyPatch(typeof(ScribeLoader))]
    [HarmonyPatch(nameof(ScribeLoader.FinalizeLoading))]
    public static class LongEventHandlerPatch
    {
        static void Prefix(ref string __state)
        {
            __state = Scribe.loader.curXmlParent.Name;
        }

        // handles the client faction
        // called after cross refs and right before map finalization
        static void Postfix(string __state)
        {
            if (ServerMod.client == null || ServerMod.server != null) return;
            if (Current.ProgramState != ProgramState.MapInitializing || __state != "game") return;

            FinalizeFactions();
        }

        static void FinalizeFactions()
        {
            ServerModWorldComp comp = Find.World.GetComponent<ServerModWorldComp>();
            if (ServerMod.clientFaction != null)
            {
                XmlNode node = Scribe.loader.curXmlParent.OwnerDocument.ImportNode(ServerMod.clientFaction.DocumentElement["clientFaction"], true);
                Scribe.loader.curXmlParent.AppendChild(node);
                Log.Message("Appended client faction to " + Scribe.loader.curXmlParent.Name);
                Faction newFaction = null;
                Scribe_Deep.Look(ref newFaction, "clientFaction");

                Find.FactionManager.Add(newFaction);
                comp.playerFactions[ServerMod.username] = newFaction;

                Log.Message("Added client faction: " + newFaction.loadID);

                ServerMod.clientFaction = null;
            }

            Faction.OfPlayer.def = FactionDefOf.Outlander;
            Faction clientFaction = comp.playerFactions[ServerMod.username];
            clientFaction.def = FactionDefOf.PlayerColony;
            Find.GameInitData.playerFaction = clientFaction;

            Log.Message("Client faction: " + clientFaction.Name + " / " + clientFaction.GetUniqueLoadID());
        }
    }

    [HarmonyPatch(typeof(CaravanArrivalAction_AttackSettlement))]
    [HarmonyPatch(nameof(CaravanArrivalAction_AttackSettlement.Arrived))]
    public static class AttackSettlementPatch
    {
        static FieldInfo settlementField = typeof(CaravanArrivalAction_AttackSettlement).GetField("settlement", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(CaravanArrivalAction_AttackSettlement __instance, Caravan caravan)
        {
            if (ServerMod.client == null) return true;

            Settlement settlement = (Settlement) settlementField.GetValue(__instance);
            string username = Find.World.GetComponent<ServerModWorldComp>().GetUsername(settlement.Faction);
            if (username == null) return true;

            ServerMod.client.Send(Packets.CLIENT_ENCOUNTER_REQUEST, BitConverter.GetBytes(settlement.Tile));

            return false;
        }
    }

}
