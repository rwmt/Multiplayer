using HarmonyLib;
using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Multiplayer.Client.Comp;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public record GameData(XmlDocument SaveData, byte[] SemiPersistent);

    [HotSwappable]
    public static class SaveLoad
    {
        public static GameData SaveAndReload()
        {
            //SimpleProfiler.Start();
            Multiplayer.reloading = true;

            var worldGridSaved = Find.WorldGrid;
            var worldRendererSaved = Find.World.renderer;
            var tweenedPos = new Dictionary<int, Vector3>();
            var drawers = new Dictionary<int, MapDrawer>();
            var localFactionId = Multiplayer.RealPlayerFaction.loadID;
            var mapCmds = new Dictionary<int, Queue<ScheduledCommand>>();
            var planetRenderMode = Find.World.renderer.wantedMode;
            var chatWindow = ChatWindow.Opened;

            var selectedData = new ByteWriter();
            SyncSerialization.WriteSync(selectedData, Find.Selector.selected.OfType<ISelectable>().ToList());

            //Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;

            foreach (Map map in Find.Maps)
            {
                drawers[map.uniqueID] = map.mapDrawer;
                //RebuildRegionsAndRoomsPatch.copyFrom[map.uniqueID] = map.regionGrid;

                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    tweenedPos[p.thingIDNumber] = p.drawer.tweener.tweenedPos;

                mapCmds[map.uniqueID] = map.AsyncTime().cmds;
            }

            mapCmds[ScheduledCommand.Global] = Multiplayer.WorldComp.cmds;

            DeepProfiler.Start("Multiplayer SaveAndReload");
            //WriteElementPatch.cachedVals = new Dictionary<string, object>();
            //WriteElementPatch.id = 0;
            var gameDoc = SaveGame();
            var semiPersistent = WriteSemiPersistent();
            var gameData = new GameData(gameDoc, semiPersistent);
            DeepProfiler.End();
            //Log.Message($"Saving took {WriteElementPatch.cachedVals.Count} {WriteElementPatch.cachedVals.FirstOrDefault()}");

            MapDrawerRegenPatch.copyFrom = drawers;
            WorldGridCachePatch.copyFrom = worldGridSaved;
            WorldGridExposeDataPatch.copyFrom = worldGridSaved;
            WorldRendererCachePatch.copyFrom = worldRendererSaved;

            MusicManagerPlay musicManager = null;
            if (Find.MusicManagerPlay.gameObjectCreated)
            {
                var musicTransform = Find.MusicManagerPlay.audioSource.transform;
                if (musicTransform.parent == Find.Root.soundRoot.sourcePool.sourcePoolCamera.cameraSourcesContainer.transform)
                    musicTransform.parent = musicTransform.parent.parent;

                musicManager = Find.MusicManagerPlay;
            }

            //SpawnSetupPatch.total = 0;
            //SpawnSetupPatch.total2 = new long[SpawnSetupPatch.total2.Length];

            LoadInMainThread(gameData);

            if (musicManager != null)
                Current.Root_Play.musicManagerPlay = musicManager;

            Multiplayer.RealPlayerFaction = Find.FactionManager.GetById(localFactionId);

            foreach (Map m in Find.Maps)
            {
                foreach (Pawn p in m.mapPawns.AllPawnsSpawned)
                {
                    if (tweenedPos.TryGetValue(p.thingIDNumber, out Vector3 v))
                    {
                        p.drawer.tweener.tweenedPos = v;
                        p.drawer.tweener.lastDrawFrame = Time.frameCount;
                    }
                }

                m.AsyncTime().cmds = mapCmds[m.uniqueID];
            }

            if (chatWindow != null)
                Find.WindowStack.Add_KeepRect(chatWindow);

            var selectedReader = new ByteReader(selectedData.ToArray()) { context = new MpContext() { map = Find.CurrentMap } };
            Find.Selector.selected = SyncSerialization.ReadSync<List<ISelectable>>(selectedReader).AllNotNull().Cast<object>().ToList();

            Find.World.renderer.wantedMode = planetRenderMode;
            Multiplayer.WorldComp.cmds = mapCmds[ScheduledCommand.Global];

            Multiplayer.reloading = false;
            //SimpleProfiler.Pause();

            //Log.Message($"allocs {(double)SpawnSetupPatch.total2.Sum() / Stopwatch.Frequency * 1000} ({SpawnSetupPatch.total2.Select((l,i) => $"{SpawnSetupPatch.methods[i]}: {(double)l / Stopwatch.Frequency * 1000}").Join(delimiter: "\n")}) {SpawnSetupPatch.total} {AllocsPrefixClass.allocs} {CustomXmlElement.n} {CustomXmlElement.m} {CustomXmlElement.n - CustomXmlElement.m} {(double)CustomXmlElement.n/CustomXmlElement.m}");

            return gameData;
        }

        public static void LoadInMainThread(GameData gameData)
        {
            DeepProfiler.Start("Multiplayer LoadInMainThread");

            ClearState();
            MemoryUtility.ClearAllMapsAndWorld();

            LoadPatch.gameToLoad = gameData;

            CancelRootPlayStartLongEvents.cancel = true;
            Find.Root.Start();
            CancelRootPlayStartLongEvents.cancel = false;

            // SaveCompression enabled in the patch
            SavedGameLoaderNow.LoadGameFromSaveFileNow(null);

            Log.Message($"loading stack {FactionContext.stack.Count}");

            DeepProfiler.End();
        }

        private static void ClearState()
        {
            if (Find.MusicManagerPlay != null)
            {
                var pool = Find.SoundRoot.sourcePool;

                foreach (var sustainer in Find.SoundRoot.sustainerManager.AllSustainers.ToList())
                    sustainer.Cleanup();

                // todo destroy other game objects?
                UnityEngine.Object.Destroy(pool.sourcePoolCamera.cameraSourcesContainer);
                UnityEngine.Object.Destroy(pool.sourcePoolWorld.sourcesWorld[0].gameObject);
            }
        }

        public static byte[] WriteSemiPersistent()
        {
            var writer = new ByteWriter();

            writer.WriteInt32(Find.Maps.Count);
            foreach (var map in Find.Maps)
            {
                writer.WriteInt32(map.uniqueID);

                var mapWriter = new ByteWriter();
                map.MpComp().WriteSemiPersistent(mapWriter);
                writer.WritePrefixedBytes(mapWriter.ToArray());
            }

            return writer.ToArray();
        }

        public static void ReadSemiPersistent(byte[] data)
        {
            if (data.Length == 0) return;

            var reader = new ByteReader(data);

            var mapCount = reader.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                var mapId = reader.ReadInt32();
                var mapData = reader.ReadPrefixedBytes();

                Find.Maps.FirstOrDefault(m => m.uniqueID == mapId)?.MpComp().ReadSemiPersistent(new ByteReader(mapData));
            }
        }

        public static XmlDocument SaveGame()
        {
            SaveCompression.doSaveCompression = true;

            ScribeUtil.StartWritingToDoc();

            Scribe.EnterNode("savegame");
            ScribeMetaHeaderUtility.WriteMetaHeader();
            Scribe.EnterNode("game");
            int currentMapIndex = Current.Game.currentMapIndex;
            Scribe_Values.Look(ref currentMapIndex, "currentMapIndex", -1);
            Current.Game.ExposeSmallComponents();
            World world = Current.Game.World;
            Scribe_Deep.Look(ref world, "world");
            List<Map> maps = Find.Maps;
            Scribe_Collections.Look(ref maps, "maps", LookMode.Deep);
            Find.CameraDriver.Expose();
            Scribe.ExitNode();

            SaveCompression.doSaveCompression = false;

            return ScribeUtil.FinishWritingToDoc();
        }

        public static void CacheGameData(GameData data)
        {
            XmlNode gameNode = data.SaveData.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            var cache = Multiplayer.session.cache;

            cache.mapData.Clear();
            cache.mapCmds.Clear();

            foreach (XmlNode mapNode in mapsNode)
            {
                int id = int.Parse(mapNode["uniqueID"].InnerText);
                byte[] mapData = ScribeUtil.XmlToByteArray(mapNode);
                cache.mapData[id] = mapData;
                cache.mapCmds[id] = new List<ScheduledCommand>(Find.Maps.First(m => m.uniqueID == id).AsyncTime().cmds);
            }

            gameNode["currentMapIndex"].RemoveFromParent();
            mapsNode.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(data.SaveData);
            cache.cachedAtTime = TickPatch.Timer;
            cache.gameData = gameData;
            cache.semiPersistentData = data.SemiPersistent;
            cache.mapCmds[ScheduledCommand.Global] = new List<ScheduledCommand>(Multiplayer.WorldComp.cmds);
        }

        public static void SendCurrentGameData(bool async)
        {
            var cache = Multiplayer.session.cache;
            var mapsData = new Dictionary<int, byte[]>(cache.mapData);
            var gameData = cache.gameData;
            var semiPersistent = cache.semiPersistentData;

            void Send()
            {
                var writer = new ByteWriter();

                writer.WriteInt32(mapsData.Count);
                foreach (var mapData in mapsData)
                {
                    writer.WriteInt32(mapData.Key);
                    writer.WritePrefixedBytes(GZipStream.CompressBuffer(mapData.Value));
                }

                writer.WritePrefixedBytes(GZipStream.CompressBuffer(gameData));
                writer.WritePrefixedBytes(GZipStream.CompressBuffer(semiPersistent));

                byte[] data = writer.ToArray();

                OnMainThread.Enqueue(() => Multiplayer.Client.SendFragmented(Packets.Client_AutosavedData, data));
            };

            if (async)
                ThreadPool.QueueUserWorkItem(c => Send());
            else
                Send();
        }
    }

    [HarmonyPatch(typeof(SavedGameLoaderNow))]
    [HarmonyPatch(nameof(SavedGameLoaderNow.LoadGameFromSaveFileNow))]
    [HarmonyPatch(new[] { typeof(string) })]
    public static class LoadPatch
    {
        public static GameData gameToLoad;

        static bool Prefix()
        {
            if (gameToLoad == null) return true;

            SaveCompression.doSaveCompression = true;

            try
            {
                ScribeUtil.StartLoading(gameToLoad.SaveData);
                ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);
                Scribe.EnterNode("game");
                Current.Game = new Game();
                Current.Game.LoadGame(); // calls Scribe.loader.FinalizeLoading()
                SaveLoad.ReadSemiPersistent(gameToLoad.SemiPersistent);
            }
            finally
            {
                SaveCompression.doSaveCompression = false;
                gameToLoad = null;
            }

            Log.Message("Game loaded");

            if (Multiplayer.Client != null)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    // Inits all caches
                    foreach (ITickable tickable in TickPatch.AllTickables.Where(t => !(t is ConstantTicker)))
                        tickable.Tick();

                    if (!Current.Game.Maps.Any())
                    {
                        MemoryUtility.UnloadUnusedUnityAssets();
                        Find.World.renderer.RegenerateAllLayersNow();
                    }
                });
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.ExposeSmallComponents))]
    static class GameExposeComponentsPatch
    {
        static void Prefix(Game __instance)
        {
            if (Multiplayer.Client == null) return;

            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Multiplayer.game = new MultiplayerGame();

            if (Scribe.mode is LoadSaveMode.LoadingVars or LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref Multiplayer.game.gameComp, "mpGameComp", __instance);

                if (Multiplayer.game.gameComp == null)
                {
                    Log.Warning($"No {nameof(MultiplayerGameComp)} during loading/saving");
                    Multiplayer.game.gameComp = new MultiplayerGameComp(__instance);
                }
            }
        }
    }

    [HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
    static class ClearAllPatch
    {
        static void Postfix()
        {
            CacheAverageTileTemperature.Clear();
            Multiplayer.game?.OnDestroy();
            Multiplayer.game = null;
        }
    }

    [HarmonyPatch(typeof(World), nameof(World.ExposeComponents))]
    static class SaveWorldComp
    {
        static void Postfix(World __instance)
        {
            if (Multiplayer.Client == null) return;

            if (Scribe.mode is LoadSaveMode.LoadingVars or LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref Multiplayer.game.worldComp, "mpWorldComp", __instance);

                if (Multiplayer.game.worldComp == null)
                {
                    Log.Warning($"No {nameof(MultiplayerWorldComp)} during loading/saving");
                    Multiplayer.game.worldComp = new MultiplayerWorldComp(__instance);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.ExposeComponents))]
    static class SaveMapComps
    {
        static void Postfix(Map __instance)
        {
            if (Multiplayer.Client == null) return;

            var asyncTime = __instance.AsyncTime();
            var comp = __instance.MpComp();

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref asyncTime, "mpAsyncTime", __instance);
                Scribe_Deep.Look(ref comp, "mpMapComp", __instance);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (asyncTime == null)
                {
                    Log.Error($"{typeof(AsyncTimeComp)} missing during loading");
                    // This is just so the game doesn't completely freeze
                    asyncTime = new AsyncTimeComp(__instance);
                }

                Multiplayer.game.asyncTimeComps.Add(asyncTime);

                if (comp == null)
                {
                    Log.Error($"{typeof(MultiplayerMapComp)} missing during loading");
                    comp = new MultiplayerMapComp(__instance);
                }

                Multiplayer.game.mapComps.Add(comp);
            }
        }
    }

    [HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.MapComponentTick))]
    static class MapCompTick
    {
        static void Postfix(Map map)
        {
            if (Multiplayer.Client == null) return;
            map.MpComp()?.DoTick();
        }
    }

    [HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.FinalizeInit))]
    static class MapCompFinalizeInit
    {
        static void Postfix(Map map)
        {
            if (Multiplayer.Client == null) return;
            map.AsyncTime()?.FinalizeInit();
        }
    }

    [HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.MapComponentUpdate))]
    static class MapCompUpdate
    {
        static void Postfix(Map map)
        {
            if (Multiplayer.Client == null) return;
            map.AsyncTime()?.Update();
        }
    }

    [HarmonyPatch(typeof(WorldComponentUtility), nameof(WorldComponentUtility.FinalizeInit))]
    static class WorldCompFinalizeInit
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.WorldComp.FinalizeInit();
        }
    }

    [HarmonyPatch(typeof(LoadedObjectDirectory), nameof(LoadedObjectDirectory.RegisterLoaded))]
    static class FixRegisterLoaded
    {
        static bool Prefix(LoadedObjectDirectory __instance, ref ILoadReferenceable reffable)
        {
            string text = "[excepted]";
            try
            {
                text = reffable.GetUniqueLoadID();
            }
            catch
            {
            }

            return !__instance.allObjectsByLoadID.ContainsKey(text);
        }
    }

}
