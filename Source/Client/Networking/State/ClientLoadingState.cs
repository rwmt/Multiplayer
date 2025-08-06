using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ionic.Zlib;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client;

public enum LoadingState
{
    Waiting,
    Downloading
}

public class ClientLoadingState(ConnectionBase connection) : ClientBaseState(connection)
{
    public LoadingState subState = LoadingState.Waiting;
    public uint WorldExpectedSize { get; private set; }
    public uint WorldReceivedSize { get; private set; }
    public float DownloadProgress => (float)WorldReceivedSize / WorldExpectedSize;
    public int DownloadProgressPercent => (int)(DownloadProgress * 100);
    public int DownloadSpeedKBps
    {
        get
        {
            if (downloadCheckpoints.Count == 0) return -1;
            var firstCheckpoint = downloadCheckpoints.First();
            var lastCheckpoint = downloadCheckpoints.Last();
            var timeTakenMs = Utils.MillisNow - firstCheckpoint.Item1;
            var timeTakenSecs = Math.Max(1, timeTakenMs / 1000);
            var downloadedBytes = lastCheckpoint.Item2 - firstCheckpoint.Item2;
            return (int)(downloadedBytes / 1000 / timeTakenSecs);
        }
    }

    private List<(long, uint)> downloadCheckpoints = new(capacity: 64);
    private Stopwatch downloadTimeStopwatch = new();

    [PacketHandler(Packets.Server_WorldDataStart)]
    public void HandleWorldDataStart(ByteReader data)
    {
        subState = LoadingState.Downloading;
        connection.Lenient = false; // Lenient is set while rejoining
        downloadTimeStopwatch.Start();
    }

    [FragmentedPacketHandler(Packets.Server_WorldData)]
    public void HandleWorldDataFragment(FragmentedPacket packet)
    {
        WorldExpectedSize = packet.ExpectedSize;
        WorldReceivedSize = packet.ReceivedSize;
        if (downloadCheckpoints.Count == downloadCheckpoints.Capacity)
            downloadCheckpoints.RemoveAt(0);
        downloadCheckpoints.Add((Utils.MillisNow, packet.ReceivedSize));
    }

    [PacketHandler(Packets.Server_WorldData, allowFragmented: true)]
    public void HandleWorldData(ByteReader data)
    {
        var downloadMs = downloadTimeStopwatch.ElapsedMilliseconds;
        downloadTimeStopwatch.Reset();
        Log.Message($"Game data size: {data.Length}. Took {downloadMs}ms to receive.");

        int factionId = data.ReadInt32();
        Multiplayer.session.myFactionId = factionId;

        int tickUntil = data.ReadInt32();
        int remoteSentCmds = data.ReadInt32();
        bool serverFrozen = data.ReadBool();

        byte[] worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
        byte[] sessionData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());

        var mapCmdsDict = new Dictionary<int, List<ScheduledCommand>>();
        var mapDataDict = new Dictionary<int, byte[]>();
        List<int> mapsToLoad = new List<int>();

        int mapCmdsCount = data.ReadInt32();
        for (int i = 0; i < mapCmdsCount; i++)
        {
            int mapId = data.ReadInt32();

            int mapCmdsLen = data.ReadInt32();
            List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (int j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            mapCmdsDict[mapId] = mapCmds;
        }

        int mapDataCount = data.ReadInt32();
        for (int i = 0; i < mapDataCount; i++)
        {
            int mapId = data.ReadInt32();
            byte[] rawMapData = data.ReadPrefixedBytes();

            byte[] mapData = GZipStream.UncompressBuffer(rawMapData);
            mapDataDict[mapId] = mapData;
            mapsToLoad.Add(mapId);
        }

        Session.dataSnapshot = new GameDataSnapshot(
            0,
            worldData,
            sessionData,
            mapDataDict,
            mapCmdsDict
        );

        TickPatch.tickUntil = tickUntil;
        Multiplayer.session.receivedCmds = remoteSentCmds;
        Multiplayer.session.remoteTickUntil = tickUntil;
        TickPatch.serverFrozen = serverFrozen;

        int syncInfos = data.ReadInt32();
        for (int i = 0; i < syncInfos; i++)
            Session.initialOpinions.Add(ClientSyncOpinion.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

        Log.Message(syncInfos > 0
            ? $"Initial sync opinions: {Session.initialOpinions.First().startTick}...{Session.initialOpinions.Last().startTick}"
            : "No initial sync opinions");

        TickPatch.SetSimulation(
            toTickUntil: true,
            onFinish: () => Multiplayer.Client.Send(Packets.Client_WorldReady),
            cancelButtonKey: "Quit",
            onCancel: GenScene.GoToMainMenu // Calls StopMultiplayer through a patch
        );

        Stopwatch watch = Stopwatch.StartNew();
        Loader.ReloadGame(mapsToLoad, true, false);
        var loadingMs = watch.ElapsedMilliseconds;
        Log.Message($"Loaded game in {loadingMs}ms");
        connection.ChangeState(ConnectionStateEnum.ClientPlaying);
    }
}
