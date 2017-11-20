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
using Verse.AI;
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

                ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);

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
            page.nextAct = () => Settle();

            Find.WindowStack.Add(page);
            MemoryUtility.UnloadUnusedUnityAssets();
            Find.World.renderer.RegenerateAllLayersNow();
        }

        private static void Settle()
        {
            LongEventHandler.QueueLongEvent(() =>
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

                ClientPlayingState.SyncClientWorldObj(factionBase);
            }, "Generating map", false, null);
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
                ServerAction action = Find.TickManager.CurTimeSpeed == TimeSpeed.Paused ? ServerAction.PAUSE : ServerAction.UNPAUSE;
                ServerMod.client.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { action });
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
    public static class FinalizeLoadingPatch
    {
        static void Prefix(ref string __state)
        {
            __state = Scribe.loader.curXmlParent.Name;
        }

        // called after cross refs and right before map finalization
        static void Postfix(string __state)
        {
            ScribeUtil.loading = false;

            if (Current.ProgramState != ProgramState.MapInitializing || __state != "game") return;

            foreach (Faction f in Find.FactionManager.AllFactions)
                ScribeUtil.crossRefs.RegisterLoaded(f);

            if (ServerMod.client == null || ServerMod.server != null) return;

            FinalizeFactions();
        }

        static void FinalizeFactions()
        {
            ServerModWorldComp comp = Find.World.GetComponent<ServerModWorldComp>();

            if (ServerMod.clientFaction != null)
            {
                ScribeUtil.StartLoading(ServerMod.clientFaction);
                ScribeUtil.SupplyCrossRefs();
                Faction newFaction = null;
                Scribe_Deep.Look(ref newFaction, "clientFaction");
                ScribeUtil.FinishLoading();

                Find.FactionManager.Add(newFaction);
                comp.playerFactions[ServerMod.username] = newFaction;

                Log.Message("Added client faction: " + newFaction.loadID);

                ServerMod.clientFaction = null;
            }

            Faction.OfPlayer.def = FactionDefOf.Outlander;
            Faction clientFaction = comp.playerFactions[ServerMod.username];
            clientFaction.def = FactionDefOf.PlayerColony;
            Find.GameInitData.playerFaction = clientFaction;

            // todo
            foreach (Faction current in Find.FactionManager.AllFactionsListForReading)
            {
                if (current == clientFaction) continue;
                current.TryMakeInitialRelationsWith(clientFaction);
            }

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

            Settlement settlement = (Settlement)settlementField.GetValue(__instance);
            string username = Find.World.GetComponent<ServerModWorldComp>().GetUsername(settlement.Faction);
            if (username == null) return true;

            ServerMod.client.Send(Packets.CLIENT_ENCOUNTER_REQUEST, BitConverter.GetBytes(settlement.Tile));

            return false;
        }
    }

    [HarmonyPatch(typeof(Settlement))]
    [HarmonyPatch(nameof(Settlement.ShouldRemoveMapNow))]
    public static class ShouldRemoveMap
    {
        static void Postfix(ref bool __result)
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(FactionBaseDefeatUtility))]
    [HarmonyPatch("IsDefeated")]
    public static class IsDefeated
    {
        static void Postfix(ref bool __result)
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.StartJob))]
    public static class JobTrackerPatch
    {
        public static bool addingJob;

        public static FieldInfo pawnField = typeof(Pawn_JobTracker).GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic);

        static bool Prefix(Pawn_JobTracker __instance, Job newJob)
        {
            if (ServerMod.client == null) return true;
            if (addingJob) return true;
            Pawn pawn = (Pawn)pawnField.GetValue(__instance);
            if (!IsPawnOwner(pawn)) return false;

            if (__instance.curJob == null || __instance.curJob.expiryInterval != -2)
            {
                PawnTempData.Get(pawn).actualJob = __instance.curJob;
                PawnTempData.Get(pawn).actualJobDriver = __instance.curDriver;

                JobRequest jobRequest = new JobRequest()
                {
                    job = newJob,
                    mapId = pawn.Map.uniqueID,
                    pawnId = pawn.thingIDNumber
                };

                ScribeUtil.StartWriting();
                Scribe.EnterNode("data");
                Scribe_Deep.Look(ref jobRequest, "job");
                byte[] jobData = ScribeUtil.FinishWriting();

                byte[] data = BitConverter.GetBytes((int)ServerAction.JOB).Append(jobData);
                ServerMod.client.Send(Packets.CLIENT_ACTION_REQUEST, data);
            }
            else
            {
                Log.Message("job start while idle");
            }

            __instance.curJob = newJob;
            __instance.curDriver = newJob.MakeDriver(pawn);
            if (!__instance.curDriver.TryMakePreToilReservations())
                Log.Message("new job pre toil fail");
            newJob.expiryInterval = -2;

            return false;
        }

        public static bool IsPawnOwner(Pawn pawn)
        {
            return (pawn.Faction != null && pawn.Faction == Faction.OfPlayer) || pawn.Map.IsPlayerHome;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class JobTrackerEnd
    {
        static void Prefix(Pawn_JobTracker __instance, JobCondition condition, ref State __state)
        {
            Pawn pawn = (Pawn)JobTrackerPatch.pawnField.GetValue(__instance);

            if (ServerMod.client == null) return;

            Log.Message("end job: " + pawn + " " + __instance.curJob + " " + condition);

            if (PawnTempData.Get(pawn).actualJob == null || __instance.curJob == PawnTempData.Get(pawn).actualJob) return;

            __state = new State()
            {
                job = __instance.curJob,
                driver = __instance.curDriver
            };

            __instance.curJob = PawnTempData.Get(pawn).actualJob;
            __instance.curDriver = PawnTempData.Get(pawn).actualJobDriver;
        }

        static void Postfix(Pawn_JobTracker __instance, State __state)
        {
            if (__state == null) return;

            Pawn pawn = (Pawn)JobTrackerPatch.pawnField.GetValue(__instance);
            if (PawnTempData.Get(pawn).actualJobDriver != __instance.curDriver)
            {
                Log.Message("cleaned actual job: " + PawnTempData.Get(pawn).actualJob + " " + pawn);
                PawnTempData.Get(pawn).actualJobDriver = null;
                PawnTempData.Get(pawn).actualJob = null;
            }

            __instance.curJob = __state.job;
            __instance.curDriver = __state.driver;
        }

        private class State
        {
            public Job job;
            public JobDriver driver;
        }
    }

    [HarmonyPatch(typeof(JobDriver))]
    [HarmonyPatch(nameof(JobDriver.DriverTick))]
    public static class JobDriverPatch
    {
        static FieldInfo startField = typeof(JobDriver).GetField("startTick", BindingFlags.Instance | BindingFlags.NonPublic);

        static bool Prefix(JobDriver __instance)
        {
            if (__instance.job.expiryInterval != -2) return true;

            __instance.job.startTick = Find.TickManager.TicksGame;
            startField.SetValue(__instance, Find.TickManager.TicksGame);

            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch("CleanupCurrentJob")]
    public static class Clear
    {
        static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            Pawn pawn = (Pawn)JobTrackerPatch.pawnField.GetValue(__instance);
            if (__instance.curJob != null && __instance.curJob.expiryInterval == -2)
                Log.Warning("cleanup " + JobTrackerPatch.addingJob + " " + __instance.curJob + " " + condition + " " + pawn + " " + __instance.curJob.expiryInterval);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.JobTrackerTick))]
    public static class JobTrackerTick
    {
        static void Prefix(Pawn_JobTracker __instance, ref State __state)
        {
            Pawn pawn = (Pawn)JobTrackerPatch.pawnField.GetValue(__instance);
            if (PawnTempData.Get(pawn).actualJobDriver == null) return;

            __state = new State()
            {
                job = __instance.curJob,
                driver = __instance.curDriver
            };

            __instance.curJob = PawnTempData.Get(pawn).actualJob;
            __instance.curDriver = PawnTempData.Get(pawn).actualJobDriver;
        }

        static void Postfix(Pawn_JobTracker __instance, State __state)
        {
            if (__state == null) return;

            Pawn pawn = (Pawn)JobTrackerPatch.pawnField.GetValue(__instance);
            if (PawnTempData.Get(pawn).actualJobDriver != __instance.curDriver)
            {
                PawnTempData.Get(pawn).actualJobDriver = null;
                PawnTempData.Get(pawn).actualJob = null;
            }

            __instance.curJob = __state.job;
            __instance.curDriver = __state.driver;
        }

        private class State
        {
            public Job job;
            public JobDriver driver;
        }
    }

    public class JobRequest : IExposable
    {
        public Job job;
        public int mapId;
        public int pawnId;

        public void ExposeData()
        {
            Scribe_Values.Look(ref mapId, "map");
            Scribe_Values.Look(ref pawnId, "pawn");
            Scribe_Deep.Look(ref job, "job");
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.SpawnSetup))]
    public static class ThingSpawnPatch
    {
        static void Postfix(Thing __instance)
        {
            if (__instance.def.HasThingIDNumber)
                ScribeUtil.crossRefs.RegisterLoaded(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.DeSpawn))]
    public static class ThingDeSpawnPatch
    {
        static void Postfix(Thing __instance)
        {
            ScribeUtil.crossRefs.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.SpawnSetup))]
    public static class WorldObjectSpawnPatch
    {
        static void Postfix(WorldObject __instance)
        {
            ScribeUtil.crossRefs.RegisterLoaded(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.PostRemove))]
    public static class WorldObjectRemovePatch
    {
        static void Postfix(WorldObject __instance)
        {
            ScribeUtil.crossRefs.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(FactionManager))]
    [HarmonyPatch(nameof(FactionManager.Add))]
    public static class FactionAddPatch
    {
        static void Postfix(Faction faction)
        {
            ScribeUtil.crossRefs.RegisterLoaded(faction);

            foreach (Map map in Find.Maps)
                map.pawnDestinationReservationManager.RegisterFaction(faction);

            Log.Message("new faction " + faction);
        }
    }

    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch(nameof(Game.DeinitAndRemoveMap))]
    public static class RemoveMapPatch
    {
        static void Postfix(Map map)
        {
            ScribeUtil.crossRefs.UnregisterMap(map);
        }
    }

    [HarmonyPatch(typeof(MemoryUtility))]
    [HarmonyPatch(nameof(MemoryUtility.ClearAllMapsAndWorld))]
    public static class ClearAllPatch
    {
        static void Postfix()
        {
            ScribeUtil.crossRefs = null;
            Log.Message("Removed all cross refs");
        }
    }

    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch("FillComponents")]
    public static class FillComponentsPatch
    {
        static void Postfix()
        {
            ScribeUtil.crossRefs = new CrossRefSupply();
            Log.Message("New cross refs");
        }
    }

    [HarmonyPatch(typeof(LoadedObjectDirectory))]
    [HarmonyPatch(nameof(LoadedObjectDirectory.RegisterLoaded))]
    public static class LoadedObjectsRegisterPatch
    {
        static bool Prefix(LoadedObjectDirectory __instance, ILoadReferenceable reffable)
        {
            if (!(__instance is CrossRefSupply)) return true;

            string key = reffable.GetUniqueLoadID();
            if (ScribeUtil.crossRefs.GetDict().ContainsKey(key)) return false;

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                ScribeUtil.crossRefs.tempKeys.Add(key);

            ScribeUtil.crossRefs.GetDict().Add(key, reffable);

            return false;
        }
    }

    [HarmonyPatch(typeof(LoadedObjectDirectory))]
    [HarmonyPatch(nameof(LoadedObjectDirectory.Clear))]
    public static class LoadedObjectsClearPatch
    {
        static bool Prefix(LoadedObjectDirectory __instance)
        {
            if (!(__instance is CrossRefSupply)) return true;

            ScribeUtil.crossRefsField.SetValue(Scribe.loader.crossRefs, ScribeUtil.defaultCrossRefs);

            foreach (string temp in ScribeUtil.crossRefs.tempKeys)
                ScribeUtil.crossRefs.Unregister(temp);
            ScribeUtil.crossRefs.tempKeys.Clear();

            return false;
        }
    }

    [HarmonyPatch(typeof(CompressibilityDeciderUtility))]
    [HarmonyPatch(nameof(CompressibilityDeciderUtility.IsSaveCompressible))]
    public static class SaveCompressible
    {
        static void Postfix(ref bool __result)
        {
            if (ServerMod.savingForEncounter)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(GenSpawn))]
    [HarmonyPatch(nameof(GenSpawn.Spawn))]
    [HarmonyPatch(new Type[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(bool) })]
    public static class GenSpawnPatch
    {
        static void Postfix(ref Thing __result)
        {
            if (__result == null || ServerMod.client == null) return;

            if (__result is Blueprint)
            {
                ScribeUtil.StartWriting();
                Scribe.EnterNode("data");
                Scribe_Deep.Look(ref __result, "thing");
                byte[] data = ScribeUtil.FinishWriting();

                ServerMod.client.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { ServerAction.SPAWN_THING, data });

                return;
            }
        }
    }

}
