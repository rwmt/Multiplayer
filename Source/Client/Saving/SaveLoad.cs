using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;
using Multiplayer.Client.Saving;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public record TempGameData(XmlDocument SaveData, byte[] SemiPersistent);


    public static class SaveLoad
    {
        public static TempGameData SaveAndReload()
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

            DeepProfiler.Start("Multiplayer SaveAndReload: Save");
            //WriteElementPatch.cachedVals = new Dictionary<string, object>();
            //WriteElementPatch.id = 0;
            var gameData = SaveGameData();
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

            Multiplayer.game.ChangeRealPlayerFaction(Find.FactionManager.GetById(localFactionId));

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

        public static void LoadInMainThread(TempGameData gameData)
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

        public static TempGameData SaveGameData()
        {
            var gameDoc = SaveGameToDoc();
            var semiPersistent = SemiPersistent.WriteSemiPersistent();
            return new TempGameData(gameDoc, semiPersistent);
        }

        public static XmlDocument SaveGameToDoc()
        {
            SaveCompression.doSaveCompression = true;

            try
            {
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
            }
            finally
            {
                SaveCompression.doSaveCompression = false;
            }

            return ScribeUtil.FinishWritingToDoc();
        }

        public static GameDataSnapshot CreateGameDataSnapshot(TempGameData data)
        {
            XmlNode gameNode = data.SaveData.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            var dataSnapshot = new GameDataSnapshot();

            foreach (XmlNode mapNode in mapsNode)
            {
                int id = int.Parse(mapNode["uniqueID"].InnerText);
                byte[] mapData = ScribeUtil.XmlToByteArray(mapNode);
                dataSnapshot.mapData[id] = mapData;
                dataSnapshot.mapCmds[id] = new List<ScheduledCommand>(Find.Maps.First(m => m.uniqueID == id).AsyncTime().cmds);
            }

            gameNode["currentMapIndex"].RemoveFromParent();
            mapsNode.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(data.SaveData);
            dataSnapshot.cachedAtTime = TickPatch.Timer;
            dataSnapshot.gameData = gameData;
            dataSnapshot.semiPersistentData = data.SemiPersistent;
            dataSnapshot.mapCmds[ScheduledCommand.Global] = new List<ScheduledCommand>(Multiplayer.WorldComp.cmds);

            return dataSnapshot;
        }

        public static void SendGameData(GameDataSnapshot data, bool async)
        {
            var cache = Multiplayer.session.dataSnapshot;
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

                OnMainThread.Enqueue(() => Multiplayer.Client?.SendFragmented(Packets.Client_WorldDataUpload, data));
            };

            if (async)
                ThreadPool.QueueUserWorkItem(c => Send());
            else
                Send();
        }
    }

}
