extern alias zip;
using HarmonyLib;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Util;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class SyncCoordinator
    {
        public bool ShouldCollect => !Multiplayer.IsReplay;

        private ClientSyncOpinion OpinionInBuilding
        {
            get
            {
                if (currentOpinion != null)
                    return currentOpinion;

                currentOpinion = new ClientSyncOpinion(TickPatch.Timer)
                {
                    isLocalClientsOpinion = true
                };

                return currentOpinion;
            }
        }

        public readonly List<ClientSyncOpinion> knownClientOpinions = new();

        public ClientSyncOpinion currentOpinion;

        private int lastValidTick = -1;
        private bool arbiterWasPlayingOnLastValidTick;

        /// <summary>
        /// Adds a client opinion to the <see cref="knownClientOpinions"/> list and checks that it matches the most recent currently in there. If not, a desync event is fired.
        /// </summary>
        /// <param name="newOpinion">The <see cref="ClientSyncOpinion"/> to add and check.</param>
        public void AddClientOpinionAndCheckDesync(ClientSyncOpinion newOpinion)
        {
            //If we've already desynced, don't even bother
            if (Multiplayer.session.desynced) return;

            //If we're simulating, again, don't bother
            if (TickPatch.Simulating) return;

            //If this is the first client opinion we have nothing to compare it with, so just add it
            if (knownClientOpinions.Count == 0)
            {
                knownClientOpinions.Add(newOpinion);
                return;
            }

            if (knownClientOpinions[0].isLocalClientsOpinion == newOpinion.isLocalClientsOpinion)
            {
                knownClientOpinions.Add(newOpinion);
                if (knownClientOpinions.Count > 30)
                    RemoveAndClearFirst();
            }
            else
            {
                //Remove all opinions that started before this one, as it's the most up to date one
                while (knownClientOpinions.Count > 0 && knownClientOpinions[0].startTick < newOpinion.startTick)
                    RemoveAndClearFirst();

                //If there are none left, we don't need to compare this new one
                if (knownClientOpinions.Count == 0)
                {
                    knownClientOpinions.Add(newOpinion);
                }
                else if (knownClientOpinions.First().startTick == newOpinion.startTick)
                {
                    //If these two contain the same tick range - i.e. they start at the same time, because they should continue to the current tick, then do a comparison.
                    var oldOpinion = knownClientOpinions.RemoveFirst();

                    //Actually do the comparison to find any desync
                    var desyncMessage = oldOpinion.CheckForDesync(newOpinion);

                    if (desyncMessage != null)
                    {
                        MpLog.Log($"Desynced after tick {lastValidTick}: {desyncMessage}");
                        Multiplayer.session.desynced = true;
                        OnMainThread.Enqueue(() => HandleDesync(oldOpinion, newOpinion, desyncMessage));
                    }
                    else
                    {
                        //Update fields
                        lastValidTick = oldOpinion.startTick;
                        arbiterWasPlayingOnLastValidTick = Multiplayer.session.ArbiterPlaying;

                        oldOpinion.Clear();
                    }
                }
            }
        }

        private void RemoveAndClearFirst()
        {
            var opinion = knownClientOpinions.RemoveFirst();
            opinion.Clear();
        }

        /// <summary>
        /// Called by <see cref="AddClientOpinionAndCheckDesync"/> if the newly added opinion doesn't match with what other ones.
        /// </summary>
        /// <param name="oldOpinion">The first up-to-date client opinion present in <see cref="knownClientOpinions"/>, that disagreed with the new one</param>
        /// <param name="newOpinion">The opinion passed to <see cref="AddClientOpinionAndCheckDesync"/> that disagreed with the currently known opinions.</param>
        /// <param name="desyncMessage">The error message that explains exactly what desynced.</param>
        private void HandleDesync(ClientSyncOpinion oldOpinion, ClientSyncOpinion newOpinion, string desyncMessage)
        {
            //Identify which of the two sync infos is local, and which is the remote.
            var local = oldOpinion.isLocalClientsOpinion ? oldOpinion : newOpinion;
            var remote = !oldOpinion.isLocalClientsOpinion ? oldOpinion : newOpinion;

            var localTraces = GetDiffAndTraceMessage(local, remote, out var diffAt);
            Multiplayer.Client.Send(Packets.Client_Desynced, local.startTick, diffAt);
            Multiplayer.session.desyncTracesFromHost = null;

            MpUI.ClearWindowStack();
            Find.WindowStack.Add(new DesyncedWindow(desyncMessage, localTraces));
        }

        private string GetDiffAndTraceMessage(ClientSyncOpinion local, ClientSyncOpinion remote, out int diffAt)
        {
            //Find the length of whichever stack trace is shorter.
            diffAt = -1;
            var localCount = local.desyncStackTraceHashes.Count;
            var remoteCount = remote.desyncStackTraceHashes.Count;
            int count = Math.Min(localCount, remoteCount);

            //Find the point at which the hashes differ - this is where the desync occurred.
            for (int i = 0; i < count; i++)
                if (local.desyncStackTraceHashes[i] != remote.desyncStackTraceHashes[i])
                {
                    diffAt = i;
                    break;
                }

            var traceMessage = "";

            if (diffAt == -1)
            {
                if (count == 0)
                {
                    diffAt = 0;
                    return $"No traces (remote: {remoteCount}, local: {localCount})";
                }

                traceMessage = "Note: trace hashes are equal between local and remote\n\n";
                diffAt = count - 1;
            }

            traceMessage += local.GetFormattedStackTracesForRange(diffAt);
            return traceMessage;
        }

        // Called from DesyncedWindow
        public void WriteDesyncInfo(string localTraces)
        {
            var watch = Stopwatch.StartNew();

            try
            {
                var desyncFilePath = FindFileNameForNextDesyncFile();
                using var zip = new ZipFile(Path.Combine(Multiplayer.DesyncsDir, desyncFilePath + ".zip"));

                zip.AddEntry("desync_info", GetDesyncDetails());
                zip.AddEntry("local_traces.txt", localTraces);
                zip.AddEntry("host_traces.txt", Multiplayer.session.desyncTracesFromHost ?? "No host traces");

                var extraLogs = LogGenerator.PrepareLogData();
                if (extraLogs != null) zip.AddEntry("local_logs.txt", extraLogs);

                zip.Save();
            }
            catch (Exception e)
            {
                Log.Error($"Exception writing desync info: {e}");
            }

            Log.Message($"Desync info writing took {watch.ElapsedMilliseconds}");
        }

        private string FindFileNameForNextDesyncFile()
        {
            const string FilePrefix = "Desync-";
            const string FileExtension = ".zip";

            //Find all current existing desync zips
            var files = new DirectoryInfo(Multiplayer.DesyncsDir).GetFiles($"{FilePrefix}*{FileExtension}" );

            const int MaxFiles = 10;

            //Delete any pushing us over the limit, and reserve room for one more
            if (files.Length > MaxFiles - 1)
                files.OrderByDescending(f => f.LastWriteTime).Skip(MaxFiles - 1).Do(DeleteFileSilent);

            //Find the current max desync number
            int max = 0;
            foreach (var f in files)
                if (int.TryParse(f.Name.Substring(FilePrefix.Length, f.Name.Length - FilePrefix.Length - FileExtension.Length), out int result) && result > max)
                    max = result;

            return $"{FilePrefix}{max + 1:00}";
        }

        private static void DeleteFileSilent(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Adds a random state to the commandRandomStates list
        /// </summary>
        /// <param name="state">The state to add</param>
        public void TryAddCommandRandomState(ulong state)
        {
            if (!ShouldCollect) return;
            OpinionInBuilding.TryMarkSimulating();
            OpinionInBuilding.commandRandomStates.Add((uint) (state >> 32));
        }

        /// <summary>
        /// Adds a random state to the worldRandomStates list
        /// </summary>
        /// <param name="state">The state to add</param>
        public void TryAddWorldRandomState(ulong state)
        {
            if (!ShouldCollect) return;
            OpinionInBuilding.TryMarkSimulating();
            OpinionInBuilding.worldRandomStates.Add((uint) (state >> 32));
        }

        /// <summary>
        /// Adds a random state to the list of the map random state handler for the map with the given id
        /// </summary>
        /// <param name="map">The map id to add the state to</param>
        /// <param name="state">The state to add</param>
        public void TryAddMapRandomState(int map, ulong state)
        {
            if (!ShouldCollect) return;
            OpinionInBuilding.TryMarkSimulating();
            OpinionInBuilding.GetRandomStatesForMap(map).Add((uint) (state >> 32));
        }

        /// <summary>
        /// Logs the current stack so that in the event of a desync we have some stack traces.
        /// </summary>
        /// <param name="info">Any additional message to be logged with the stack</param>
        public void TryAddStackTraceForDesyncLog(string info = null)
        {
            if (!ShouldCollect) return;

            OpinionInBuilding.TryMarkSimulating();

            //Get the current stack trace
            var trace = new StackTrace(2, true);
            var hash = trace.Hash() /*^ (info?.GetHashCode() ?? 0)*/;

            OpinionInBuilding.desyncStackTraces.Add(new StackTraceLogItemObj {
                stackTrace = trace,
                tick = TickPatch.Timer,
                hash = hash,
                additionalInfo = info,
            });

            // Track & network trace hash, for comparison with other opinions.
            OpinionInBuilding.desyncStackTraceHashes.Add(hash);
        }

        public void TryAddStackTraceForDesyncLogRaw(StackTraceLogItemRaw item, int depth, int hashIn, string moreInfo = null)
        {
            if (!ShouldCollect) return;

            OpinionInBuilding.TryMarkSimulating();

            item.depth = depth;
            item.iters = (int)Rand.iterations;
            item.tick = TickPatch.Timer;
            item.factionName = Faction.OfPlayer?.Name ?? string.Empty;
            item.moreInfo = moreInfo;

            var thing = ThingContext.Current;
            if (thing != null)
            {
                item.thingDef = thing.def;
                item.thingId = thing.thingIDNumber;
            }

            var hash = Gen.HashCombineInt(hashIn, depth, item.iters, 1);
            item.hash = hash;

            OpinionInBuilding.desyncStackTraces.Add(item);

            // Track & network trace hash, for comparison with other opinions.
            OpinionInBuilding.desyncStackTraceHashes.Add(hash);
        }

        public static string MethodNameWithIL(string rawName)
        {
            // Note: The names currently don't include IL locations so the code is commented out

            // at Verse.AI.JobDriver.ReadyForNextToil () [0x00000] in <c847e073cda54790b59d58357cc8cf98>:0
            // =>
            // at Verse.AI.JobDriver.ReadyForNextToil () [0x00000]
            // rawName = rawName.Substring(0, rawName.LastIndexOf(']') + 1);

            return rawName;
        }

        public static string MethodNameWithoutIL(string rawName)
        {
            // Note: The names currently don't include IL locations so the code is commented out

            // at Verse.AI.JobDriver.ReadyForNextToil () [0x00000] in <c847e073cda54790b59d58357cc8cf98>:0
            // =>
            // at Verse.AI.JobDriver.ReadyForNextToil ()
            // rawName = rawName.Substring(0, rawName.LastIndexOf('['));

            return rawName;
        }

        private string GetDesyncDetails()
        {
            var desyncInfo = new StringBuilder();

            desyncInfo
                .AppendLine("###Tick Data###")
                .AppendLine($"Arbiter Connected And Playing|||{Multiplayer.session.ArbiterPlaying}")
                .AppendLine($"Last Valid Tick - Local|||{lastValidTick}")
                .AppendLine($"Last Valid Tick - Arbiter|||{arbiterWasPlayingOnLastValidTick}")
                .AppendLine("\n###Version Data###")
                .AppendLine($"Multiplayer Mod Version|||{MpVersion.Version}")
                .AppendLine($"Rimworld Version and Rev|||{VersionControl.CurrentVersionStringWithRev}")
                .AppendLine("\n###Debug Options###")
                .AppendLine($"Multiplayer Debug Build - Client|||{MpVersion.IsDebug}")
                .AppendLine($"Multiplayer Debug Mode - Host|||{Multiplayer.GameComp.debugMode}")
                .AppendLine($"Rimworld Developer Mode - Client|||{Prefs.DevMode}")
                .AppendLine("\n###Server Info###")
                .AppendLine($"Player Count|||{Multiplayer.session.players.Count}")
                .AppendLine("\n###CPU Info###")
                .AppendLine($"Processor Name|||{SystemInfo.processorType}")
                .AppendLine($"Processor Speed (MHz)|||{SystemInfo.processorFrequency}")
                .AppendLine($"Thread Count|||{SystemInfo.processorCount}")
                .AppendLine("\n###GPU Info###")
                .AppendLine($"GPU Family|||{SystemInfo.graphicsDeviceVendor}")
                .AppendLine($"GPU Type|||{SystemInfo.graphicsDeviceType}")
                .AppendLine($"GPU Name|||{SystemInfo.graphicsDeviceName}")
                .AppendLine($"GPU VRAM|||{SystemInfo.graphicsMemorySize}")
                .AppendLine("\n###RAM Info###")
                .AppendLine($"Physical Memory Present|||{SystemInfo.systemMemorySize}")
                .AppendLine("\n###OS Info###")
                .AppendLine($"OS Type|||{SystemInfo.operatingSystemFamily}")
                .AppendLine($"OS Name and Version|||{SystemInfo.operatingSystem}");

            return desyncInfo.ToString();
        }
    }
}
