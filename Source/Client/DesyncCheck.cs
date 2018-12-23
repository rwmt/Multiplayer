extern alias zip;

using Harmony;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

namespace Multiplayer.Client
{
    public class SyncInfoBuffer
    {
        public List<SyncInfo> buffer = new List<SyncInfo>();

        public bool ShouldCollect => !Multiplayer.IsReplay && TickPatch.skipTo < 0;

        public SyncInfo current;
        private SyncInfo Current
        {
            get
            {
                if (current != null)
                    return current;

                current = new SyncInfo(TickPatch.Timer)
                {
                    local = true
                };

                return current;
            }
        }

        private int lastValidTick = -1;
        private bool lastValidArbiter;

        public void Add(SyncInfo info)
        {
            if (buffer.Count == 0)
            {
                buffer.Add(info);
                return;
            }

            if (buffer[0].local == info.local)
            {
                buffer.Add(info);
                if (buffer.Count > 30)
                    buffer.RemoveAt(0);
            }
            else
            {
                while (buffer.Count > 0 && buffer[0].startTick < info.startTick)
                    buffer.RemoveAt(0);

                if (buffer.Count == 0)
                {
                    buffer.Add(info);
                }
                else if (buffer.First().startTick == info.startTick)
                {
                    var first = buffer.RemoveFirst();
                    var error = first.Compare(info);

                    if (error != null)
                    {
                        MpLog.Log($"Desynced {lastValidTick}: {error}");
                        OnDesynced(first, info, error);
                    }
                    else
                    {
                        lastValidTick = first.startTick;
                        lastValidArbiter = Multiplayer.session.ArbiterPlaying;
                    }
                }
            }
        }

        private void OnDesynced(SyncInfo one, SyncInfo two, string error)
        {
            if (Multiplayer.session.desynced) return;

            Multiplayer.Client.Send(Packets.Client_Desynced);
            Multiplayer.session.desynced = true;

            var local = one.local ? one : two;
            var remote = !one.local ? one : two;

            if (local.traces.Any())
                PrintTrace(local, remote);

            try
            {
                var desyncFile = PrepareNextDesyncFile();

                var replay = Replay.ForSaving(desyncFile, Multiplayer.DesyncsDir);
                replay.WriteCurrentData();

                var savedGame = ScribeUtil.WriteExposable(Verse.Current.Game, "game", true, ScribeMetaHeaderUtility.WriteMetaHeader);

                using (var zip = replay.ZipFile)
                {
                    zip.AddEntry("sync_local", local.Serialize());
                    zip.AddEntry("sync_remote", remote.Serialize());
                    zip.AddEntry("game_snapshot", savedGame);

                    var desyncInfo = new ByteWriter();
                    desyncInfo.WriteBool(Multiplayer.session.ArbiterPlaying);
                    desyncInfo.WriteInt32(lastValidTick);
                    desyncInfo.WriteBool(lastValidArbiter);
                    desyncInfo.WriteString(MpVersion.Version);
                    desyncInfo.WriteBool(MpVersion.IsDebug);
                    desyncInfo.WriteBool(Prefs.DevMode);

                    zip.AddEntry("desync_info", desyncInfo.GetArray());
                    zip.Save();
                }
            }
            catch (Exception e)
            {
                Log.Error($"Exception writing desync info: {e}");
            }

            Find.WindowStack.windows.Clear();
            Find.WindowStack.Add(new DesyncedWindow(error));
        }

        private void PrintTrace(SyncInfo local, SyncInfo remote)
        {
            File.WriteAllText("host_traces.txt", local.traces.Join(delimiter: "\n\n"));
            Multiplayer.Client.Send(Packets.Client_Debug, local.startTick);
        }

        private string PrepareNextDesyncFile()
        {
            var files = new DirectoryInfo(Multiplayer.DesyncsDir).GetFiles("Desync-*.zip");

            const int MaxFiles = 10;
            if (files.Length > MaxFiles - 1)
                files.OrderByDescending(f => f.LastWriteTime).Skip(MaxFiles - 1).Do(f => f.Delete());

            int max = 0;
            foreach (var f in files)
                if (int.TryParse(f.Name.Substring(7, f.Name.Length - 7 - 4), out int result) && result > max)
                    max = result;

            return $"Desync-{max + 1:00}";
        }

        public void TryAddCmd(ulong state)
        {
            if (!ShouldCollect) return;
            Current.cmds.Add((uint)(state >> 32));
        }

        public void TryAddWorld(ulong state)
        {
            if (!ShouldCollect) return;
            Current.world.Add((uint)(state >> 32));
        }

        public void TryAddMap(int map, ulong state)
        {
            if (!ShouldCollect) return;
            Current.GetForMap(map).Add((uint)(state >> 32));
        }

        public void TryAddStackTrace()
        {
            if (!ShouldCollect) return;

            var trace = new StackTrace(4);
            Current.traces.Add(trace);
            current.traceHashes.Add(trace.Hash());
        }
    }

    public class SyncInfo
    {
        public bool local;
        public int startTick;
        public List<uint> cmds = new List<uint>();
        public List<uint> world = new List<uint>();
        public List<SyncMapInfo> maps = new List<SyncMapInfo>();

        public List<StackTrace> traces = new List<StackTrace>();
        public List<int> traceHashes = new List<int>();

        public SyncInfo(int startTick)
        {
            this.startTick = startTick;
        }

        public string Compare(SyncInfo other)
        {
            if (!maps.Select(m => m.mapId).SequenceEqual(other.maps.Select(m => m.mapId)))
                return $"Map instances don't match";

            for (int i = 0; i < maps.Count; i++)
            {
                if (!maps[i].map.SequenceEqual(other.maps[i].map))
                    return $"Wrong random state on map {maps[i].mapId}";
            }

            if (!world.SequenceEqual(other.world))
                return "Wrong random state for the world";

            if (!cmds.SequenceEqual(other.cmds))
                return "Random state from commands doesn't match";

            if (traceHashes.Any() && other.traceHashes.Any() && !traceHashes.SequenceEqual(other.traceHashes))
                return "Trace hashes don't match";

            return null;
        }

        public List<uint> GetForMap(int mapId)
        {
            var result = maps.Find(m => m.mapId == mapId);
            if (result != null) return result.map;
            maps.Add(result = new SyncMapInfo(mapId));
            return result.map;
        }

        public byte[] Serialize()
        {
            var writer = new ByteWriter();

            writer.WriteInt32(startTick);
            writer.WritePrefixedUInts(cmds);
            writer.WritePrefixedUInts(world);

            writer.WriteInt32(maps.Count);
            foreach (var map in maps)
            {
                writer.WriteInt32(map.mapId);
                writer.WritePrefixedUInts(map.map);
            }

            writer.WritePrefixedInts(traceHashes);

            return writer.GetArray();
        }

        public static SyncInfo Deserialize(ByteReader data)
        {
            var startTick = data.ReadInt32();

            var cmds = new List<uint>(data.ReadPrefixedUInts());
            var world = new List<uint>(data.ReadPrefixedUInts());

            var maps = new List<SyncMapInfo>();
            int mapCount = data.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                int mapId = data.ReadInt32();
                var mapData = new List<uint>(data.ReadPrefixedUInts());
                maps.Add(new SyncMapInfo(mapId) { map = mapData });
            }

            var traceHashes = new List<int>(data.ReadPrefixedInts());

            return new SyncInfo(startTick)
            {
                cmds = cmds,
                world = world,
                maps = maps,
                traceHashes = traceHashes
            };
        }
    }

    public class SyncMapInfo
    {
        public int mapId;
        public List<uint> map = new List<uint>();

        public SyncMapInfo(int mapId)
        {
            this.mapId = mapId;
        }
    }

    public static class DesyncDebugInfo
    {
        public static string Get(Replay replay)
        {
            var text = new StringBuilder();

            using (var zip = replay.ZipFile)
            {
                try
                {
                    text.AppendLine("[info]");
                    text.AppendLine(zip["info"].GetString());
                    text.AppendLine();
                }
                catch { }

                SyncInfo local = null;
                try
                {
                    local = PrintSyncInfo(text, zip, "sync_local");
                }
                catch { }

                SyncInfo remote = null;
                try
                {
                    remote = PrintSyncInfo(text, zip, "sync_remote");
                }
                catch { }

                try
                {
                    text.AppendLine("[desync_info]");
                    var desyncInfo = new ByteReader(zip["desync_info"].GetBytes());
                    text.AppendLine($"Arbiter online: {desyncInfo.ReadBool()}");
                    text.AppendLine($"Last valid tick: {desyncInfo.ReadInt32()}");
                    text.AppendLine($"Last valid arbiter online: {desyncInfo.ReadBool()}");
                    text.AppendLine($"Mod version: {desyncInfo.ReadString()}");
                    text.AppendLine($"Mod is debug: {desyncInfo.ReadBool()}");
                    text.AppendLine($"Dev mode: {desyncInfo.ReadBool()}");
                    text.AppendLine();
                }
                catch { }

                if (local != null && remote != null)
                {
                    text.AppendLine("[compare]");

                    for (int i = 0; i < Math.Min(local.maps.Count, remote.maps.Count); i++)
                    {
                        var localMap = local.maps[i].map;
                        var remoteMap = remote.maps[i].map;
                        bool equal = localMap.SequenceEqual(remoteMap);
                        text.AppendLine($"Map {local.maps[i].mapId}: {equal}");

                        if (!equal)
                            for (int j = 0; j < Math.Min(localMap.Count, remoteMap.Count); j++)
                                text.AppendLine($"{localMap[j]} {remoteMap[j]} {(localMap[j] != remoteMap[j] ? "x" : "")}");
                    }

                    text.AppendLine($"World: {local.world.SequenceEqual(remote.world)}");
                    text.AppendLine($"Cmds: {local.cmds.SequenceEqual(remote.cmds)}");
                }
            }

            return text.ToString();

            SyncInfo PrintSyncInfo(StringBuilder builder, ZipFile zip, string file)
            {
                builder.AppendLine($"[{file}]");

                var sync = SyncInfo.Deserialize(new ByteReader(zip[file].GetBytes()));
                builder.AppendLine($"Start: {sync.startTick}");
                builder.AppendLine($"Map count: {sync.maps.Count}");
                builder.AppendLine($"Last map state: {sync.maps.Select(m => $"{m.mapId}/{m.map.LastOrDefault()}/{m.map.Count}").ToStringSafeEnumerable()}");
                builder.AppendLine($"Last world state: {sync.world.LastOrDefault()}/{sync.world.Count}");
                builder.AppendLine($"Last cmd state: {sync.cmds.LastOrDefault()}/{sync.cmds.Count}");
                builder.AppendLine($"Trace hashes: {sync.traceHashes.Count}");

                builder.AppendLine();

                return sync;
            }
        }
    }

}
