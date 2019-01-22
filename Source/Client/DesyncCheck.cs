extern alias zip;

using Harmony;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

namespace Multiplayer.Client
{
    public class SyncInfoBuffer
    {
        public List<SyncInfo> buffer = new List<SyncInfo>();

        public bool ShouldCollect => !Multiplayer.IsReplay;

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
            if (Multiplayer.session.desynced) return;

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
                        Multiplayer.session.desynced = true;
                        OnMainThread.Enqueue(() => OnDesynced(first, info, error));
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
            if (TickPatch.Skipping) return;

            Multiplayer.Client.Send(Packets.Client_Desynced);

            var local = one.local ? one : two;
            var remote = !one.local ? one : two;

            if (local.traces.Any())
                PrintTrace(local, remote);

            try
            {
                var desyncFile = PrepareNextDesyncFile();

                var replay = Replay.ForSaving(Replay.ReplayFile(desyncFile, Multiplayer.DesyncsDir));
                replay.WriteCurrentData();

                var savedGame = ScribeUtil.WriteExposable(Verse.Current.Game, "game", true, ScribeMetaHeaderUtility.WriteMetaHeader);

                using (var zip = replay.ZipFile)
                {
                    zip.AddEntry("sync_local", local.Serialize());
                    zip.AddEntry("sync_remote", remote.Serialize());
                    zip.AddEntry("game_snapshot", savedGame);

                    zip.AddEntry("static_fields", MpDebugTools.StaticFieldsToString());

                    var desyncInfo = new ByteWriter();
                    desyncInfo.WriteBool(Multiplayer.session.ArbiterPlaying);
                    desyncInfo.WriteInt32(lastValidTick);
                    desyncInfo.WriteBool(lastValidArbiter);
                    desyncInfo.WriteString(MpVersion.Version);
                    desyncInfo.WriteBool(MpVersion.IsDebug);
                    desyncInfo.WriteBool(Prefs.DevMode);
                    desyncInfo.WriteInt32(Multiplayer.session.players.Count);
                    desyncInfo.WriteBool(Multiplayer.WorldComp.debugMode);

                    zip.AddEntry("desync_info", desyncInfo.ToArray());
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
            Log.Message($"Printing {local.traces.Count} traces");

            int diffAt = -1;
            int count = Math.Min(local.traceHashes.Count, remote.traceHashes.Count);

            for (int i = 0; i < count; i++)
                if (local.traceHashes[i] != remote.traceHashes[i])
                {
                    diffAt = i;
                    break;
                }

            if (diffAt == -1)
                diffAt = count;

            File.WriteAllText("local_traces.txt", local.TracesToString(diffAt - 40, diffAt + 40));
            Multiplayer.Client.Send(Packets.Client_Debug, local.startTick, diffAt - 40, diffAt + 40);
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
            Current.TryMarkSimulating();
            Current.cmds.Add((uint)(state >> 32));
        }

        public void TryAddWorld(ulong state)
        {
            if (!ShouldCollect) return;
            Current.TryMarkSimulating();
            Current.world.Add((uint)(state >> 32));
        }

        public void TryAddMap(int map, ulong state)
        {
            if (!ShouldCollect) return;
            Current.TryMarkSimulating();
            Current.GetForMap(map).Add((uint)(state >> 32));
        }

        private static MethodBase TickPatchTick = AccessTools.Method(typeof(TickPatch), nameof(TickPatch.Tick));

        public void TryAddStackTrace(string info = null, bool doTrace = true)
        {
            if (!ShouldCollect) return;

            Current.TryMarkSimulating();

            var trace = doTrace ? MpUtil.FastStackTrace(4) : new MethodBase[0];
            Current.traces.Add(new TraceInfo() { trace = trace, info = info });
            current.traceHashes.Add(trace.Hash() ^ (info?.GetHashCode() ?? 0));
        }
    }

    public class TraceInfo
    {
        public MethodBase[] trace;
        public string info;
    }

    public class SyncInfo
    {
        public bool local;

        public int startTick;
        public List<uint> cmds = new List<uint>();
        public List<uint> world = new List<uint>();
        public List<SyncMapInfo> maps = new List<SyncMapInfo>();

        public List<TraceInfo> traces = new List<TraceInfo>();
        public List<int> traceHashes = new List<int>();
        public bool simulating;

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

            if (!simulating && !other.simulating && traceHashes.Any() && other.traceHashes.Any() && !traceHashes.SequenceEqual(other.traceHashes))
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
            writer.WriteBool(simulating);

            return writer.ToArray();
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
            var playing = data.ReadBool();

            return new SyncInfo(startTick)
            {
                cmds = cmds,
                world = world,
                maps = maps,
                traceHashes = traceHashes,
                simulating = playing
            };
        }

        public void TryMarkSimulating()
        {
            if (TickPatch.Skipping)
                simulating = true;
        }

        public string TracesToString(int start, int end)
        {
            return traces.Skip(Math.Max(0, start)).Take(end - start).Join(a => a.info + "\n" + a.trace.Join(m => m.MethodDesc(), "\n"), delimiter: "\n\n");
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
                    text.AppendLine($"[header]");

                    using (var reader = new XmlTextReader(new MemoryStream(zip["game_snapshot"].GetBytes())))
                    {
                        reader.ReadToNextElement();
                        reader.ReadToNextElement();

                        text.AppendLine(reader.ReadOuterXml());
                    }
                }
                catch (Exception e)
                {
                    text.AppendLine(e.Message);
                }

                text.AppendLine();

                try
                {
                    text.AppendLine("[info]");
                    text.AppendLine(zip["info"].GetString());
                }
                catch { }

                text.AppendLine();

                SyncInfo local = null;
                try
                {
                    local = PrintSyncInfo(text, zip, "sync_local");
                }
                catch { }

                text.AppendLine();

                SyncInfo remote = null;
                try
                {
                    remote = PrintSyncInfo(text, zip, "sync_remote");
                }
                catch { }

                text.AppendLine();

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
                    text.AppendLine($"Player count: {desyncInfo.ReadInt32()}");
                    text.AppendLine($"Game debug mode: {desyncInfo.ReadBool()}");
                }
                catch { }

                text.AppendLine();

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

                text.AppendLine();

                try
                {
                    text.AppendLine("[map_cmds]");
                    foreach (var cmd in Replay.DeserializeCmds(zip["maps/000_0_cmds"].GetBytes()))
                        PrintCmdInfo(text, cmd);
                }
                catch { }

                text.AppendLine();

                try
                {
                    text.AppendLine("[world_cmds]");
                    foreach (var cmd in Replay.DeserializeCmds(zip["world/000_cmds"].GetBytes()))
                        PrintCmdInfo(text, cmd);
                }
                catch { }
            }

            return text.ToString();

            void PrintCmdInfo(StringBuilder builder, ScheduledCommand cmd)
            {
                builder.Append($"{cmd.type} {cmd.ticks} {cmd.mapId} {cmd.factionId}");

                if (cmd.type == CommandType.Sync)
                    builder.Append($" {Sync.handlers[BitConverter.ToInt32(cmd.data, 0)]}");

                builder.AppendLine();
            }

            SyncInfo PrintSyncInfo(StringBuilder builder, ZipFile zip, string file)
            {
                builder.AppendLine($"[{file}]");

                var sync = SyncInfo.Deserialize(new ByteReader(zip[file].GetBytes()));
                builder.AppendLine($"Start: {sync.startTick}");
                builder.AppendLine($"Was simulating: {sync.simulating}");
                builder.AppendLine($"Map count: {sync.maps.Count}");
                builder.AppendLine($"Last map state: {sync.maps.Select(m => $"{m.mapId}/{m.map.LastOrDefault()}/{m.map.Count}").ToStringSafeEnumerable()}");
                builder.AppendLine($"Last world state: {sync.world.LastOrDefault()}/{sync.world.Count}");
                builder.AppendLine($"Last cmd state: {sync.cmds.LastOrDefault()}/{sync.cmds.Count}");
                builder.AppendLine($"Trace hashes: {sync.traceHashes.Count}");

                return sync;
            }
        }
    }

}
