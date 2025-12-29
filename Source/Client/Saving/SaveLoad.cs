using Ionic.Zlib;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
                // When reloading in multifaction always save as a fixed faction to ensure determinism
                // The faction is chosen to be the first player faction in the FactionManager as this is what
                // Faction.OfPlayer gets set to initially during loading (in FactionManager.ExposeData -> RecacheFactions)
                if (Multiplayer.GameComp.multifaction)
                    Multiplayer.game.ChangeRealPlayerFaction(Find.FactionManager.AllFactions.First(f => f.IsPlayer), false);
                gameData = SaveGameData();
            }

            // TODO
            //MapDrawerRegenPatch.copyFrom = drawers;
            //WorldGridCachePatch.copyFrom = worldGridSaved;
            //WorldGridExposeDataPatch.copyFrom = worldGridSaved;

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
                UnityEngine.Object.Destroy(pool.sourcePoolCamera.cameraSourcesContainer);
                UnityEngine.Object.Destroy(pool.sourcePoolWorld.sourcesWorld[0].gameObject);
            }
        }

        public static TempGameData SaveGameData()
        {
            var gameDoc = SaveGameToDoc();

            byte[] sessionData;
            try
            {
                sessionData = SessionData.WriteSessionData();
            }
            catch (Exception e)
            {
                Log.Error($"Bootstrap: Exception writing session data, session will be empty: {e}");
                sessionData = Array.Empty<byte>();
            }
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
                Log.Message($"Bootstrap: SaveGameToDoc is serializing {maps?.Count ?? 0} maps");
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
            // Be defensive: XML may be missing nodes in bootstrap or due to mod patches
            XmlElement root = data.SaveData.DocumentElement;
            XmlNode gameNode = root != null ? root["game"] : null;
            XmlNode mapsNode = gameNode != null ? gameNode["maps"] : null;

            Log.Message($"Bootstrap: CreateGameDataSnapshot XML structure - root={root?.Name}, game={gameNode?.Name}, maps={mapsNode?.Name}");
            Log.Message($"Bootstrap: CreateGameDataSnapshot mapsNode children={mapsNode?.ChildNodes?.Count ?? 0}, Find.Maps.Count={Find.Maps?.Count ?? -1}");

            var mapCmdsDict = new Dictionary<int, List<ScheduledCommand>>();
            var mapDataDict = new Dictionary<int, byte[]>();

            if (mapsNode != null)
            {
                foreach (XmlNode mapNode in mapsNode)
                {
                    // Skip malformed map nodes
                    var idNode = mapNode?["uniqueID"];
                    if (idNode == null) continue;

                    int id = int.Parse(idNode.InnerText);
                    byte[] mapData = ScribeUtil.XmlToByteArray(mapNode);
                    mapDataDict[id] = mapData;

                    Log.Message($"Bootstrap: CreateGameDataSnapshot extracted map {id} ({mapData.Length} bytes)");

                    // Offline bootstrap can run without async-time comps; guard nulls and write empty cmd lists
                    var map = Find.Maps.FirstOrDefault(m => m.uniqueID == id);
                    var mapAsync = map?.AsyncTime();
                    if (mapAsync?.cmds != null)
                        mapCmdsDict[id] = new List<ScheduledCommand>(mapAsync.cmds);
                    else
                        mapCmdsDict[id] = new List<ScheduledCommand>();
                }
            }

            if (removeCurrentMapId)
            {
                var currentMapIndexNode = gameNode?["currentMapIndex"];
                if (currentMapIndexNode != null)
                    currentMapIndexNode.RemoveFromParent();
            }

            // Remove map nodes from the game XML to form world-only data
            mapsNode?.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(data.SaveData);
            // World/global commands may be unavailable offline; default to empty list
            if (Multiplayer.AsyncWorldTime != null)
                mapCmdsDict[ScheduledCommand.Global] = new List<ScheduledCommand>(Multiplayer.AsyncWorldTime.cmds);
            else
                mapCmdsDict[ScheduledCommand.Global] = new List<ScheduledCommand>();

            // Note: We no longer fall back to vanilla .rws extraction here.
            // The bootstrap flow now hosts a temporary MP session and uses the standard MP save,
            // which reliably produces proper replay snapshots including maps.

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
                    writer.WritePrefixedBytes(Ionic.Zlib.GZipStream.CompressBuffer(mapData.Value));
                }

                writer.WritePrefixedBytes(Ionic.Zlib.GZipStream.CompressBuffer(gameData));
                writer.WritePrefixedBytes(Ionic.Zlib.GZipStream.CompressBuffer(sessionData));

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
