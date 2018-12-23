using Harmony;
using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public static class SaveLoad
    {
        public static XmlDocument SaveAndReload()
        {
            Multiplayer.reloading = true;

            WorldGrid worldGridSaved = Find.WorldGrid;
            WorldRenderer worldRendererSaved = Find.World.renderer;
            var tweenedPos = new Dictionary<int, Vector3>();
            var drawers = new Dictionary<int, MapDrawer>();
            int localFactionId = Multiplayer.RealPlayerFaction.loadID;
            var mapCmds = new Dictionary<int, Queue<ScheduledCommand>>();

            //RealPlayerFaction = DummyFaction;

            foreach (Map map in Find.Maps)
            {
                drawers[map.uniqueID] = map.mapDrawer;
                //RebuildRegionsAndRoomsPatch.copyFrom[map.uniqueID] = map.regionGrid;

                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    tweenedPos[p.thingIDNumber] = p.drawer.tweener.tweenedPos;

                mapCmds[map.uniqueID] = map.AsyncTime().cmds;
            }

            mapCmds[ScheduledCommand.Global] = Multiplayer.WorldComp.cmds;

            Stopwatch watch = Stopwatch.StartNew();
            XmlDocument gameDoc = SaveGame();
            Log.Message("Saving took " + watch.ElapsedMilliseconds);

            MapDrawerRegenPatch.copyFrom = drawers;
            WorldGridCachePatch.copyFrom = worldGridSaved;
            WorldRendererCachePatch.copyFrom = worldRendererSaved;

            LoadInMainThread(gameDoc);

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

            Multiplayer.WorldComp.cmds = mapCmds[ScheduledCommand.Global];

            SaveCompression.doSaveCompression = false;
            Multiplayer.reloading = false;

            return gameDoc;
        }

        public static void LoadInMainThread(XmlDocument gameDoc)
        {
            var watch = Stopwatch.StartNew();
            MemoryUtility.ClearAllMapsAndWorld();

            LoadPatch.gameToLoad = gameDoc;

            CancelRootPlayStartLongEvents.cancel = true;
            Find.Root.Start();
            CancelRootPlayStartLongEvents.cancel = false;

            SavedGameLoaderNow.LoadGameFromSaveFileNow(null);

            Log.Message("Loading took " + watch.ElapsedMilliseconds);
        }

        public static XmlDocument SaveGame()
        {
            //SaveCompression.doSaveCompression = true;

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

        public static void CacheGameData(XmlDocument doc)
        {
            XmlNode gameNode = doc.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            OnMainThread.cachedMapData.Clear();
            OnMainThread.cachedMapCmds.Clear();

            foreach (XmlNode mapNode in mapsNode)
            {
                int id = int.Parse(mapNode["uniqueID"].InnerText);
                byte[] mapData = ScribeUtil.XmlToByteArray(mapNode);
                OnMainThread.cachedMapData[id] = mapData;
                OnMainThread.cachedMapCmds[id] = new List<ScheduledCommand>(Find.Maps.First(m => m.uniqueID == id).AsyncTime().cmds);
            }

            gameNode["currentMapIndex"].RemoveFromParent();
            mapsNode.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(doc);
            OnMainThread.cachedAtTime = TickPatch.Timer;
            OnMainThread.cachedGameData = gameData;
            OnMainThread.cachedMapCmds[ScheduledCommand.Global] = new List<ScheduledCommand>(Multiplayer.WorldComp.cmds);
        }

        public static void SendCurrentGameData(bool async)
        {
            var mapsData = new Dictionary<int, byte[]>(OnMainThread.cachedMapData);
            var gameData = OnMainThread.cachedGameData;

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

                Multiplayer.Client.SendFragmented(Packets.Client_AutosavedData, writer.GetArray());
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
        public static XmlDocument gameToLoad;

        static bool Prefix()
        {
            if (gameToLoad == null) return true;

            bool prevCompress = SaveCompression.doSaveCompression;
            //SaveCompression.doSaveCompression = true;

            ScribeUtil.StartLoading(gameToLoad);
            ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);
            Scribe.EnterNode("game");
            Current.Game = new Game();
            Current.Game.LoadGame(); // calls Scribe.loader.FinalizeLoading()

            SaveCompression.doSaveCompression = prevCompress;
            gameToLoad = null;

            Log.Message("Game loaded");

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

            return false;
        }
    }
}
