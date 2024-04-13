using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;
using Multiplayer.Client.Saving;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public record TempGameData(XmlDocument SaveData, byte[] SessionData);

    public static class SaveLoad
    {
        public static TempGameData SaveAndReload()
        {
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

            foreach (Map map in Find.Maps)
            {
                drawers[map.uniqueID] = map.mapDrawer;

                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    tweenedPos[p.thingIDNumber] = p.drawer.tweener.tweenedPos;

                mapCmds[map.uniqueID] = map.AsyncTime().cmds;
            }

            mapCmds[ScheduledCommand.Global] = Multiplayer.AsyncWorldTime.cmds;

            TempGameData gameData;
            using (DeepProfilerWrapper.Section("Multiplayer SaveAndReload: Save"))
            {
                // When reloading in multifaction always save as the spectator faction to ensure determinism
                if (Multiplayer.GameComp.multifaction)
                    Multiplayer.game.ChangeRealPlayerFaction(Multiplayer.WorldComp.spectatorFaction, false);
                gameData = SaveGameData();
            }

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
            Multiplayer.AsyncWorldTime.cmds = mapCmds[ScheduledCommand.Global];

            Multiplayer.reloading = false;

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
                Object.Destroy(pool.sourcePoolCamera.cameraSourcesContainer);
                Object.Destroy(pool.sourcePoolWorld.sourcesWorld[0].gameObject);
            }
        }

        public static TempGameData SaveGameData()
        {
            var gameDoc = SaveGameToDoc();
            var sessionData = SessionData.WriteSessionData();
            return new TempGameData(gameDoc, sessionData);
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

        public static GameDataSnapshot CreateGameDataSnapshot(TempGameData data, bool removeCurrentMapId)
        {
            XmlNode gameNode = data.SaveData.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            var mapCmdsDict = new Dictionary<int, List<ScheduledCommand>>();
            var mapDataDict = new Dictionary<int, byte[]>();

            foreach (XmlNode mapNode in mapsNode)
            {
                int id = int.Parse(mapNode["uniqueID"].InnerText);
                byte[] mapData = ScribeUtil.XmlToByteArray(mapNode);
                mapDataDict[id] = mapData;
                mapCmdsDict[id] = new List<ScheduledCommand>(Find.Maps.First(m => m.uniqueID == id).AsyncTime().cmds);
            }

            if (removeCurrentMapId)
                gameNode["currentMapIndex"].RemoveFromParent();

            mapsNode.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(data.SaveData);
            mapCmdsDict[ScheduledCommand.Global] = new List<ScheduledCommand>(Multiplayer.AsyncWorldTime.cmds);

            return new GameDataSnapshot(
                TickPatch.Timer,
                gameData,
                data.SessionData,
                mapDataDict,
                mapCmdsDict
            );
        }

        public static void SendGameData(GameDataSnapshot snapshot, bool async)
        {
            var mapsData = new Dictionary<int, byte[]>(snapshot.MapData);
            var gameData = snapshot.GameData;
            var sessionData = snapshot.SessionData;

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
                writer.WritePrefixedBytes(GZipStream.CompressBuffer(sessionData));

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
