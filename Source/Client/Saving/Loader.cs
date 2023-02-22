using System.Collections.Generic;
using System.IO;
using System.Xml;
using HarmonyLib;
using Multiplayer.Common;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client.Saving;

public static class Loader
{
    public static void ReloadGame(List<int> mapsToLoad, bool changeScene, bool forceAsyncTime)
    {
        var gameDoc = DataSnapshotToXml(Multiplayer.session.dataSnapshot, mapsToLoad);

        LoadPatch.gameToLoad = new(gameDoc, Multiplayer.session.dataSnapshot.SemiPersistentData);
        TickPatch.replayTimeSpeed = TimeSpeed.Paused;

        if (changeScene)
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = new Game
                {
                    InitData = new GameInitData
                    {
                        gameToLoad = "server"
                    }
                };

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    LongEventHandler.QueueLongEvent(() => PostLoad(forceAsyncTime), "MpSimulating", false, null);
                });
            }, "Play", "MpLoading", true, null);
        }
        else
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                SaveLoad.LoadInMainThread(LoadPatch.gameToLoad);
                PostLoad(forceAsyncTime);
            }, "MpLoading", false, null);
        }
    }

    private static void PostLoad(bool forceAsyncTime)
    {
        // If the client gets disconnected during loading
        if (Multiplayer.Client == null) return;

        Multiplayer.session.dataSnapshot = Multiplayer.session.dataSnapshot with
        {
            CachedAtTime = TickPatch.Timer
        };

        Multiplayer.session.replayTimerStart = TickPatch.Timer;

        Multiplayer.game.ChangeRealPlayerFaction(Find.FactionManager.GetById(Multiplayer.session.myFactionId));
        Multiplayer.game.myFactionLoading = null;

        if (forceAsyncTime)
            Multiplayer.game.gameComp.asyncTime = true;

        Multiplayer.WorldTime.cmds = new Queue<ScheduledCommand>(
            Multiplayer.session.dataSnapshot.MapCmds.GetValueSafe(ScheduledCommand.Global) ??
            new List<ScheduledCommand>());
        // Map cmds are added in MapAsyncTimeComp.FinalizeInit
    }

    private static XmlDocument DataSnapshotToXml(GameDataSnapshot dataSnapshot, List<int> mapsToLoad)
    {
        XmlDocument gameDoc = ScribeUtil.LoadDocument(dataSnapshot.GameData);
        XmlNode gameNode = gameDoc.DocumentElement["game"];

        foreach (int map in mapsToLoad)
        {
            using XmlReader reader = XmlReader.Create(new MemoryStream(dataSnapshot.MapData[map]));
            XmlNode mapNode = gameDoc.ReadNode(reader);
            gameNode["maps"].AppendChild(mapNode);

            if (gameNode["currentMapIndex"] == null)
                gameNode.AddNode("currentMapIndex", map.ToString());
        }

        return gameDoc;
    }
}
