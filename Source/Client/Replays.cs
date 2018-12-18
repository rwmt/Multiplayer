extern alias zip;

using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class Replay
    {
        public readonly string fileName;
        public ReplayInfo info = new ReplayInfo();

        private string folder;

        private Replay(string fileName, string folder = null)
        {
            this.fileName = fileName;
            this.folder = folder;
        }

        private string FilePath => Path.Combine(folder ?? Multiplayer.ReplaysDir, $"{fileName}.zip");

        public FileInfo File => new FileInfo(FilePath);
        public ZipFile ZipFile => new ZipFile(FilePath);

        public void WriteCurrentData()
        {
            string sectionId = info.sections.Count.ToString("D3");

            using (var zip = ZipFile)
            {
                foreach (var kv in OnMainThread.cachedMapData)
                    zip.AddEntry($"maps/{sectionId}_{kv.Key}_save", kv.Value);

                foreach (var kv in OnMainThread.cachedMapCmds)
                    if (kv.Key >= 0)
                        zip.AddEntry($"maps/{sectionId}_{kv.Key}_cmds", SerializeCmds(kv.Value));

                if (OnMainThread.cachedMapCmds.TryGetValue(ScheduledCommand.Global, out var worldCmds))
                    zip.AddEntry($"world/{sectionId}_cmds", SerializeCmds(worldCmds));

                zip.AddEntry($"world/{sectionId}_save", OnMainThread.cachedGameData);
                zip.Save();
            }

            info.sections.Add(new ReplaySection(OnMainThread.cachedAtTime, TickPatch.Timer));
            WriteInfo();
        }

        private byte[] SerializeCmds(List<ScheduledCommand> cmds)
        {
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(cmds.Count);
            foreach (var cmd in cmds)
                writer.WritePrefixedBytes(cmd.Serialize());

            return writer.GetArray();
        }

        private List<ScheduledCommand> DeserializeCmds(byte[] data)
        {
            var reader = new ByteReader(data);

            int count = reader.ReadInt32();
            var result = new List<ScheduledCommand>(count);
            for (int i = 0; i < count; i++)
                result.Add(ScheduledCommand.Deserialize(new ByteReader(reader.ReadPrefixedBytes())));

            return result;
        }

        public void WriteInfo()
        {
            using (var zip = ZipFile)
            {
                zip.UpdateEntry("info", DirectXmlSaver.XElementFromObject(info, typeof(ReplayInfo)).ToString());
                zip.Save();
            }
        }

        public void LoadInfo()
        {
            using (var zip = ZipFile)
            {
                var doc = ScribeUtil.LoadDocument(zip["info"].GetBytes());
                info = DirectXmlToObject.ObjectFromXml<ReplayInfo>(doc.DocumentElement, true);
            }
        }

        public void LoadCurrentData(int sectionId)
        {
            string sectionIdStr = sectionId.ToString("D3");

            using (var zip = ZipFile)
            {
                foreach (var mapCmds in zip.SelectEntries($"name = maps/{sectionIdStr}_*_cmds"))
                {
                    int mapId = int.Parse(mapCmds.FileName.Split('_')[1]);
                    OnMainThread.cachedMapCmds[mapId] = DeserializeCmds(mapCmds.GetBytes());
                }

                foreach (var mapSave in zip.SelectEntries($"name = maps/{sectionIdStr}_*_save"))
                {
                    int mapId = int.Parse(mapSave.FileName.Split('_')[1]);
                    OnMainThread.cachedMapData[mapId] = mapSave.GetBytes();
                }

                var worldCmds = zip[$"world/{sectionIdStr}_cmds"];
                if (worldCmds != null)
                    OnMainThread.cachedMapCmds[ScheduledCommand.Global] = DeserializeCmds(worldCmds.GetBytes());

                OnMainThread.cachedGameData = zip[$"world/{sectionIdStr}_save"].GetBytes();
            }
        }

        public static Replay ForLoading(string fileName)
        {
            return new Replay(fileName);
        }

        public static Replay ForLoading(FileInfo file)
        {
            return new Replay(Path.GetFileNameWithoutExtension(file.Name));
        }

        public static Replay ForSaving(string fileName, string folder = null)
        {
            var replay = new Replay(fileName, folder);
            replay.info.name = Multiplayer.session.gameName;
            replay.info.playerFaction = Multiplayer.session.myFactionId;
            replay.info.protocol = MpVersion.Protocol;

            return replay;
        }

        public static void LoadReplay(string name, bool toEnd = false, Action after = null)
        {
            var session = Multiplayer.session = new MultiplayerSession();
            session.client = new ReplayConnection();
            session.client.State = ConnectionStateEnum.ClientPlaying;
            session.replay = true;

            var replay = ForLoading(name);
            replay.LoadInfo();
            replay.LoadCurrentData(0);
            // todo ensure everything is read correctly

            session.myFactionId = replay.info.playerFaction;
            session.replayTimerStart = replay.info.sections[0].start;

            int tickUntil = replay.info.sections[0].end;
            session.replayTimerEnd = tickUntil;
            TickPatch.tickUntil = tickUntil;

            TickPatch.skipTo = toEnd ? tickUntil : session.replayTimerStart;
            TickPatch.afterSkip = after;

            ClientJoiningState.ReloadGame(OnMainThread.cachedMapData.Keys.ToList());
        }
    }

    public class ReplayInfo
    {
        public string name;
        public int protocol;
        public int playerFaction;

        public List<ReplaySection> sections = new List<ReplaySection>();
        public List<ReplayEvent> events = new List<ReplayEvent>();
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

    public class ReplayConnection : IConnection
    {
        protected override void SendRaw(byte[] raw, bool reliable)
        {
        }

        public override void HandleReceive(ByteReader data, bool reliable)
        {
        }

        public override void Close()
        {
        }
    }

}
