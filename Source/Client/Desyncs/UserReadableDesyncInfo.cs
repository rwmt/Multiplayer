extern alias zip;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Multiplayer.Common;
using Verse;
using zip::Ionic.Zip;

namespace Multiplayer.Client
{
    public static class UserReadableDesyncInfo
    {
        /// <summary>
        /// Attempts to generate user-readable desync info from the given replay 
        /// </summary>
        /// <param name="replay">The replay to generate the info from</param>
        /// <returns>The desync info as a human-readable string</returns>
        public static string GenerateFromReplay(Replay replay)
        {
            var text = new StringBuilder();

            //Open the replay zip
            using (var zip = replay.ZipFile)
            {
                try
                {
                    text.AppendLine("[header]");

                    using (var reader = new XmlTextReader(new MemoryStream(zip["game_snapshot"].GetBytes())))
                    {
                        //Read to the <root> element
                        reader.ReadToNextElement();
                        //Read to the <meta> element
                        reader.ReadToNextElement();

                        //Append the entire <meta> element, including game version, mod IDs, and mod names, to the text
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
                    //The info is the replay save data, including game name, protocol version, and assembly hashes
                    text.AppendLine("[info]");
                    text.AppendLine(zip["info"].GetString());
                }
                catch
                {
                }

                text.AppendLine();

                ClientSyncOpinion local = null;
                try
                {
                    //Local Client Opinion data
                    local = DeserializeAndPrintSyncInfo(text, zip, "sync_local");
                }
                catch
                {
                }

                text.AppendLine();

                ClientSyncOpinion remote = null;
                try
                {
                    //Remote Client Opinion data
                    remote = DeserializeAndPrintSyncInfo(text, zip, "sync_remote");
                }
                catch
                {
                }

                text.AppendLine();

                try
                {
                    //Desync info
                    text.AppendLine("[desync_info]");

                    //Backwards compatibility! (AKA v1 support)
                    if (zip["desync_info"].GetString().StartsWith("###"))
                        //This is a V2 file, dump as-is
                        text.AppendLine(zip["desync_info"].GetString());
                    else
                    {
                        //V1 file, parse it.
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
                }
                catch
                {
                }

                text.AppendLine();

                //Append random state comparison saved from the desync
                if (local != null && remote != null)
                {
                    text.AppendLine("[compare]");

                    for (int i = 0; i < Math.Min(local.mapStates.Count, remote.mapStates.Count); i++)
                    {
                        var localMap = local.mapStates[i].randomStates;
                        var remoteMap = remote.mapStates[i].randomStates;
                        bool equal = localMap.SequenceEqual(remoteMap);
                        text.AppendLine($"Map {local.mapStates[i].mapId}: {equal}");

                        if (!equal)
                            for (int j = 0; j < Math.Min(localMap.Count, remoteMap.Count); j++)
                                text.AppendLine($"{localMap[j]} {remoteMap[j]} {(localMap[j] != remoteMap[j] ? "x" : "")}");
                    }

                    text.AppendLine($"World: {local.worldRandomStates.SequenceEqual(remote.worldRandomStates)}");
                    text.AppendLine($"Cmds: {local.commandRandomStates.SequenceEqual(remote.commandRandomStates)}");
                }

                text.AppendLine();

                try
                {
                    //Add commands random states saved with the replay
                    text.AppendLine("[map_cmds]");
                    foreach (var cmd in Replay.DeserializeCmds(zip["maps/000_0_cmds"].GetBytes()))
                        PrintCmdInfo(text, cmd);
                }
                catch
                {
                }

                text.AppendLine();

                try
                {
                    //Add world random states saved with the replay
                    text.AppendLine("[world_cmds]");
                    foreach (var cmd in Replay.DeserializeCmds(zip["world/000_cmds"].GetBytes()))
                        PrintCmdInfo(text, cmd);
                }
                catch
                {
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// Append the data for the given command in a somewhat readable form to the provided string builder
        /// </summary>
        /// <param name="builder">The builder to append data to</param>
        /// <param name="cmd">The command to append</param>
        private static void PrintCmdInfo(StringBuilder builder, ScheduledCommand cmd)
        {
            //Add basic data
            builder.Append($"{cmd.type} {cmd.ticks} {cmd.mapId} {cmd.factionId}");

            //If this is a sync command, add data on the handler used.
            if (cmd.type == CommandType.Sync)
                builder.Append($" {Sync.handlers[BitConverter.ToInt32(cmd.data, 0)]}");

            builder.AppendLine();
        }

        /// <summary>
        /// Deserializes the sync file (Representing a Client Opinion) with the given filename inside the given zip file
        /// and dumps its data in a human-readable format to the provided string builder
        /// </summary>
        /// <param name="builder">The builder to append the data to</param>
        /// <param name="zip">The zip file that contains the file with the provided name</param>
        /// <param name="filename">The name of the sync file to dump</param>
        /// <returns>The deserialized client opinion that was dumped</returns>
        private static ClientSyncOpinion DeserializeAndPrintSyncInfo(StringBuilder builder, ZipFile zip, string filename)
        {
            builder.AppendLine($"[{filename}]");

            var sync = ClientSyncOpinion.Deserialize(new ByteReader(zip[filename].GetBytes()));
            builder.AppendLine($"Start: {sync.startTick}");
            builder.AppendLine($"Was simulating: {sync.simulating}");
            builder.AppendLine($"Map count: {sync.mapStates.Count}");
            builder.AppendLine($"Last map state: {sync.mapStates.Select(m => $"{m.mapId}/{m.randomStates.LastOrDefault()}/{m.randomStates.Count}").ToStringSafeEnumerable()}");
            builder.AppendLine($"Last world state: {sync.worldRandomStates.LastOrDefault()}/{sync.worldRandomStates.Count}");
            builder.AppendLine($"Last cmd state: {sync.commandRandomStates.LastOrDefault()}/{sync.commandRandomStates.Count}");
            builder.AppendLine($"Trace hashes: {sync.desyncStackTraceHashes.Count}");

            return sync;
        }
    }
}