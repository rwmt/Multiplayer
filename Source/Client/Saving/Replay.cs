using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public class Replay
    {
        public ReplayInfo info;

        private Replay(FileInfo file) => this.file = file;

        private FileInfo file;

        private ZipArchive CreateZipWrite() => MpZipFile.Open(file.FullName, ZipArchiveMode.Create);

        public ZipArchive OpenZipRead() => MpZipFile.Open(file.FullName, ZipArchiveMode.Read);

        public void WriteCurrentData() => WriteData(Multiplayer.session.dataSnapshot);

        public void WriteData(GameDataSnapshot gameData) => WriteData(gameData, TickPatch.Timer);

        // endTick: the tick loading the replay simulates to
        public void WriteData(GameDataSnapshot gameData, int endTick)
        {
            string sectionId = info.sections.Count.ToString("D3");
            using var zip = CreateZipWrite();

            foreach (var (mapId, mapData) in gameData.MapData)
                zip.AddEntry($"maps/{sectionId}_{mapId}_save", mapData);

            // All cmds in execution order. Map cmd entries (maps/N_M_cmds) are only read from older replays.
            zip.AddEntry($"world/{sectionId}_cmds", ScheduledCommand.SerializeCmds(gameData.Cmds));

            zip.AddEntry($"world/{sectionId}_save", gameData.GameData);
            info.sections.Add(new ReplaySection(gameData.CachedAtTime, endTick));

            zip.AddEntry("info", ReplayInfo.Write(info));
        }

        public bool LoadInfo()
        {
            using var zip = OpenZipRead();
            var infoFile = zip.GetEntry("info");
            if (infoFile == null) return false;

            info = ReplayInfo.Read(infoFile.GetBytes());

            return true;
        }

        public GameDataSnapshot LoadGameData(int sectionId)
        {
            var legacyMapCmds = new List<List<ScheduledCommand>>();
            var mapDataDict = new Dictionary<int, byte[]>();

            string sectionIdStr = sectionId.ToString("D3");

            using var zip = OpenZipRead();

            foreach (var mapCmds in zip.GetEntries($"maps/{sectionIdStr}_*_cmds"))
                legacyMapCmds.Add(ScheduledCommand.DeserializeCmds(mapCmds.GetBytes()));

            var worldCmdsEntry = zip.GetEntry($"world/{sectionIdStr}_cmds");
            var worldCmds = worldCmdsEntry != null
                ? ScheduledCommand.DeserializeCmds(worldCmdsEntry.GetBytes())
                : new List<ScheduledCommand>();

            foreach (var mapSave in zip.GetEntries($"maps/{sectionIdStr}_*_save"))
            {
                int mapId = int.Parse(mapSave.Name.Split('_')[1]);
                mapDataDict[mapId] = mapSave.GetBytes();
            }

            return new GameDataSnapshot(
                0,
                zip.GetBytes($"world/{sectionIdStr}_save"),
                Array.Empty<byte>(),
                mapDataDict,
                ScheduledCommand.MergeLegacyCmds(worldCmds, legacyMapCmds)
            );
        }

        public static FileInfo SavedReplayFile(string fileName, string folder = null)
            => new(Path.Combine(folder ?? Multiplayer.ReplaysDir, $"{fileName}.zip"));

        public static Replay ForLoading(string fileName) => ForLoading(SavedReplayFile(fileName));
        public static Replay ForLoading(FileInfo file) => new(file);

        public static Replay ForSaving(string fileName) => ForSaving(SavedReplayFile(fileName));
        public static Replay ForSaving(FileInfo file) => ForSaving(file, CurrentInfo());

        public static Replay ForSaving(FileInfo file, ReplayInfo info) => new(file) { info = info };

        // Info of the current session, without sections
        public static ReplayInfo CurrentInfo() => new()
        {
            name = Multiplayer.session.gameName,
            playerFaction = Multiplayer.session.myFactionId,
            spectatorFaction = Multiplayer.WorldComp.spectatorFaction.loadID,
            protocol = MpVersion.Protocol,
            rwVersion = VersionControl.CurrentVersionStringWithRev,
            modIds = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToList(),
            modNames = LoadedModManager.RunningModsListForReading.Select(m => m.Name).ToList(),
            asyncTime = Multiplayer.GameComp.asyncTime,
            multifaction = Multiplayer.GameComp.multifaction
        };

        public static void LoadReplay(FileInfo file, bool toEnd = false, Action after = null, Action cancel = null, string simTextKey = null, bool showTimeline = true)
        {
            var session = new MultiplayerSession
            {
                client = new ReplayConnection(),
                replay = true
            };

            Multiplayer.session = session;
            session.client.ChangeState(ConnectionStateEnum.ClientPlaying);

            var replay = ForLoading(file);
            replay.LoadInfo();

            var sectionIndex = toEnd ? (replay.info.sections.Count - 1) : 0;
            session.dataSnapshot = replay.LoadGameData(sectionIndex);

            // todo ensure everything is read correctly

            session.myFactionId = replay.info.playerFaction;
            session.replayTimerStart = replay.info.sections[sectionIndex].start;
            session.showTimeline = showTimeline;

            int tickUntil = replay.info.sections[sectionIndex].end;
            session.replayTimerEnd = tickUntil;
            TickPatch.tickUntil = tickUntil;

            TickPatch.SetSimulation(
                toEnd ? tickUntil : session.replayTimerStart,
                onFinish: after,
                onCancel: cancel,
                simTextKey: simTextKey
            );

            Loader.ReloadGame(session.dataSnapshot.MapData.Keys.ToList(), true, replay.info.asyncTime);
        }
    }
}
