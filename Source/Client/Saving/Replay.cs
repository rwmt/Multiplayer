using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Multiplayer.Client.Saving;
using Multiplayer.Common.Util;
using Verse;

namespace Multiplayer.Client
{
    public class Replay
    {
        public ReplayInfo info;

        private Replay(FileInfo file)
        {
            File = file;
        }

        public FileInfo File { get; }

        public ZipArchive CreateZipWrite()
        {
            return MpZipFile.Open(File.FullName, ZipArchiveMode.Create);
        }

        public ZipArchive OpenZipRead()
        {
            return MpZipFile.Open(File.FullName, ZipArchiveMode.Read);
        }

        public void WriteCurrentData()
        {
            WriteData(Multiplayer.session.dataSnapshot);
        }

        public void WriteData(GameDataSnapshot gameData)
        {
            string sectionId = info.sections.Count.ToString("D3");
            using var zip = CreateZipWrite();

            foreach (var (mapId, mapData) in gameData.MapData)
                zip.AddEntry($"maps/{sectionId}_{mapId}_save", mapData);

            foreach (var (mapId, mapCmdData) in gameData.MapCmds)
                if (mapId >= 0)
                    zip.AddEntry($"maps/{sectionId}_{mapId}_cmds", ScheduledCommand.SerializeCmds(mapCmdData));

            if (gameData.MapCmds.TryGetValue(ScheduledCommand.Global, out var worldCmds))
                zip.AddEntry($"world/{sectionId}_cmds", ScheduledCommand.SerializeCmds(worldCmds));

            zip.AddEntry($"world/{sectionId}_save", gameData.GameData);
            info.sections.Add(new ReplaySection(gameData.CachedAtTime, TickPatch.Timer));

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
            var mapCmdsDict = new Dictionary<int, List<ScheduledCommand>>();
            var mapDataDict = new Dictionary<int, byte[]>();

            string sectionIdStr = sectionId.ToString("D3");

            using var zip = OpenZipRead();

            foreach (var mapCmds in zip.GetEntries($"maps/{sectionIdStr}_*_cmds"))
            {
                int mapId = int.Parse(mapCmds.Name.Split('_')[1]);
                mapCmdsDict[mapId] = ScheduledCommand.DeserializeCmds(mapCmds.GetBytes());
            }

            var worldCmds = zip.GetEntry($"world/{sectionIdStr}_cmds");
            if (worldCmds != null)
                mapCmdsDict[ScheduledCommand.Global] = ScheduledCommand.DeserializeCmds(worldCmds.GetBytes());

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
                mapCmdsDict
            );
        }

        public static FileInfo SavedReplayFile(string fileName, string folder = null)
            => new(Path.Combine(folder ?? Multiplayer.ReplaysDir, $"{fileName}.zip"));

        public static Replay ForLoading(string fileName) => ForLoading(SavedReplayFile(fileName));
        public static Replay ForLoading(FileInfo file) => new(file);

        public static Replay ForSaving(string fileName) => ForSaving(SavedReplayFile(fileName));
        public static Replay ForSaving(FileInfo file)
        {
            var replay = new Replay(file)
            {
                info = new ReplayInfo()
                {
                    name = Multiplayer.session.gameName,
                    playerFaction = Multiplayer.session.myFactionId,
                    protocol = MpVersion.Protocol,
                    rwVersion = VersionControl.CurrentVersionStringWithRev,
                    modIds = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToList(),
                    modNames = LoadedModManager.RunningModsListForReading.Select(m => m.Name).ToList(),
                    asyncTime = Multiplayer.GameComp.asyncTime,
                    multifaction = Multiplayer.GameComp.multifaction
                }
            };

            return replay;
        }

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
