extern alias zip;

using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

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
        public ZipFile ZipFile => new ZipFile(File.FullName);

        public void WriteCurrentData()
        {
            WriteData(Multiplayer.session.dataSnapshot);
        }

        public void WriteData(GameDataSnapshot gameData)
        {
            string sectionId = info.sections.Count.ToString("D3");
            using var zip = ZipFile;

            foreach (var (mapId, mapData) in gameData.mapData)
                zip.AddEntry($"maps/{sectionId}_{mapId}_save", mapData);

            foreach (var (mapId, mapCmdData) in gameData.mapCmds)
                if (mapId >= 0)
                    zip.AddEntry($"maps/{sectionId}_{mapId}_cmds", SerializeCmds(mapCmdData));

            if (gameData.mapCmds.TryGetValue(ScheduledCommand.Global, out var worldCmds))
                zip.AddEntry($"world/{sectionId}_cmds", SerializeCmds(worldCmds));

            zip.AddEntry($"world/{sectionId}_save", gameData.gameData);
            info.sections.Add(new ReplaySection(gameData.cachedAtTime, TickPatch.Timer));

            zip.UpdateEntry("info", DirectXmlSaver.XElementFromObject(info, typeof(ReplayInfo)).ToString());
            zip.Save();
        }

        public static byte[] SerializeCmds(List<ScheduledCommand> cmds)
        {
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(cmds.Count);
            foreach (var cmd in cmds)
                writer.WritePrefixedBytes(ScheduledCommand.Serialize(cmd));

            return writer.ToArray();
        }

        public static List<ScheduledCommand> DeserializeCmds(byte[] data)
        {
            var reader = new ByteReader(data);

            int count = reader.ReadInt32();
            var result = new List<ScheduledCommand>(count);
            for (int i = 0; i < count; i++)
                result.Add(ScheduledCommand.Deserialize(new ByteReader(reader.ReadPrefixedBytes())));

            return result;
        }

        public bool LoadInfo()
        {
            using var zip = ZipFile;
            var infoFile = zip["info"];
            if (infoFile == null) return false;

            var doc = ScribeUtil.LoadDocument(infoFile.GetBytes());
            info = DirectXmlToObject.ObjectFromXml<ReplayInfo>(doc.DocumentElement, true);

            return true;
        }

        public void LoadCurrentData(int sectionId)
        {
            var dataSnapshot = new GameDataSnapshot();
            string sectionIdStr = sectionId.ToString("D3");

            using var zip = ZipFile;

            foreach (var mapCmds in zip.SelectEntries($"name = maps/{sectionIdStr}_*_cmds"))
            {
                int mapId = int.Parse(mapCmds.FileName.Split('_')[1]);
                dataSnapshot.mapCmds[mapId] = DeserializeCmds(mapCmds.GetBytes());
            }

            foreach (var mapSave in zip.SelectEntries($"name = maps/{sectionIdStr}_*_save"))
            {
                int mapId = int.Parse(mapSave.FileName.Split('_')[1]);
                dataSnapshot.mapData[mapId] = mapSave.GetBytes();
            }

            var worldCmds = zip[$"world/{sectionIdStr}_cmds"];
            if (worldCmds != null)
                dataSnapshot.mapCmds[ScheduledCommand.Global] = DeserializeCmds(worldCmds.GetBytes());

            dataSnapshot.gameData = zip[$"world/{sectionIdStr}_save"].GetBytes();
            dataSnapshot.semiPersistentData = new byte[0];

            Multiplayer.session.dataSnapshot = dataSnapshot;
        }

        public static FileInfo ReplayFile(string fileName, string folder = null)
            => new(Path.Combine(folder ?? Multiplayer.ReplaysDir, $"{fileName}.zip"));

        public static Replay ForLoading(string fileName) => ForLoading(ReplayFile(fileName));
        public static Replay ForLoading(FileInfo file) => new Replay(file);

        public static Replay ForSaving(string fileName) => ForSaving(ReplayFile(fileName));
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
                }
            };

            return replay;
        }

        public static void LoadReplay(FileInfo file, bool toEnd = false, Action after = null, Action cancel = null, string simTextKey = null)
        {
            var session = Multiplayer.session = new MultiplayerSession();
            session.client = new ReplayConnection();
            session.client.State = ConnectionStateEnum.ClientPlaying;
            session.replay = true;

            var replay = ForLoading(file);
            replay.LoadInfo();

            var sectionIndex = toEnd ? (replay.info.sections.Count - 1) : 0;
            replay.LoadCurrentData(sectionIndex);

            // todo ensure everything is read correctly

            session.myFactionId = replay.info.playerFaction;
            session.replayTimerStart = replay.info.sections[sectionIndex].start;

            int tickUntil = replay.info.sections[sectionIndex].end;
            session.replayTimerEnd = tickUntil;
            TickPatch.tickUntil = tickUntil;

            TickPatch.SetSimulation(
                toEnd ? tickUntil : session.replayTimerStart,
                onFinish: after,
                onCancel: cancel,
                simTextKey: simTextKey
            );

            ClientJoiningState.ReloadGame(session.dataSnapshot.mapData.Keys.ToList(), true, replay.info.asyncTime);
        }
    }

    public class ReplayInfo
    {
        public string name;
        public int protocol;
        public int playerFaction;

        public List<ReplaySection> sections = new List<ReplaySection>();
        public List<ReplayEvent> events = new List<ReplayEvent>();

        public string rwVersion;
        public List<string> modIds;
        public List<string> modNames;
        public List<int> modAssemblyHashes; // Unused, here to satisfy DirectXmlToObject on old saves

        public bool asyncTime;
    }

    public class ReplaySection
    {
        public int start;
        public int end;

        public ReplaySection()
        {
        }

        public ReplaySection(int start, int end)
        {
            this.start = start;
            this.end = end;
        }
    }

    public class ReplayEvent
    {
        public string name;
        public int time;
        public Color color;
    }

    public class ReplayConnection : ConnectionBase
    {
        protected override void SendRaw(byte[] raw, bool reliable)
        {
        }

        public override void HandleReceive(ByteReader data, bool reliable)
        {
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }
    }

}
