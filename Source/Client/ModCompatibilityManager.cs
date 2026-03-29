#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using LudeonTK;
using Multiplayer.Common;
using RestSharp;
using Steamworks;
using Verse;

namespace Multiplayer.Client
{
    public static class ModCompatibilityManager
    {
        static ModCompatibilityManager()
        {
            Task.Run(UpdateModCompatibilityDb);
        }

        public static bool? fetchSuccess;

        private static Dictionary<long, ModCompatibility> workshopLookup = new();
        public static Dictionary<string, ModCompatibility> nameLookup = new();

        private const string CacheFileName = "compat.json";
        private static readonly string CacheFilePath = Path.Combine(Multiplayer.CacheDir, CacheFileName);
        private class CacheRoot
        {
            public string? CachedDate { get; set; }
            public string? CachedETag { get; set; }
            public List<ModCompatibility>? Mods { get; set; }
        }

        private static async Task<CacheRoot?> TryLoadCachedDb()
        {
            if (!File.Exists(CacheFilePath)) return null;
            var data = await File.ReadAllTextAsync(CacheFilePath);
            try
            {
                return SimpleJson.DeserializeObject<CacheRoot>(data);
            }
            catch (Exception e)
            {
                Log.Warning($"MP: Failed to deserialize {CacheFileName}:\n{e}");
                return null;
            }
        }
        private static async Task TrySaveCachedDb(CacheRoot cache)
        {
            try
            {
                var data = SimpleJson.SerializeObject(cache);
                await File.WriteAllTextAsync(CacheFilePath, data);
            }
            catch (Exception e)
            {
                Log.Warning($"MP: Failed to save cache {CacheFileName}:\n{e}");
            }
        }

        [DebugAction(category = "Multiplayer", allowedGameStates = AllowedGameStates.Entry)]
        private static void ClearCompatCacheFile() => File.Delete(CacheFilePath);
        [DebugAction(category = "Multiplayer", allowedGameStates = AllowedGameStates.Entry)]
        private static void ClearLoadedCompatData() {
            workshopLookup.Clear();
            nameLookup.Clear();
            fetchSuccess = null;
        }

        [DebugAction(category = "Multiplayer", allowedGameStates = AllowedGameStates.Entry)]
        private static void UpdateModCompatCache() => Task.Run(UpdateModCompatibilityDb);

        // Requires clearing loaded compat data to work (because of the static constructor which runs before it's
        // possible to switch this value).
        [TweakValue(category: "Multiplayer")] private static bool simulateOffline = false;

        private static async Task UpdateModCompatibilityDb()
        {
            var client = new RestClient("https://bot.rimworldmultiplayer.com/");
            client.AddDefaultHeader("X-Multiplayer-Version", MpVersion.Version);
            try
            {
                var cached = await TryLoadCachedDb();
                if (cached?.Mods != null)
                {
                    ServerLog.Log("MP: displaying cached mod compat while updating...");
                    SetupFrom(cached.Mods);
                }

                if (simulateOffline) throw new Exception("Simulating offline state");

                var req = new RestRequest("mod-compatibility?version=1.1&format=metadata")
                {
                    RequestFormat = DataFormat.Json
                };
                if (cached?.CachedDate is { } date)
                {
                    req.AddHeader("If-Modified-Since", date);
                }
                if (cached?.CachedETag is { } etag)
                {
                    req.AddHeader("If-None-Match", etag);
                }

                var stopwatch = Stopwatch.StartNew();
                var resp = client.Get(req);
                if (resp.ErrorException != null) throw resp.ErrorException;
                List<ModCompatibility>? modCompatibilities;
                if (resp.StatusCode == HttpStatusCode.NotModified)
                {
                    modCompatibilities = cached!.Mods!;
                }
                else
                {
                    if (resp.StatusCode != HttpStatusCode.OK)
                    {
                        Log.Warning(
                            $"MP: received unexpected status code {resp.StatusCode} when fetching mod compatibility. Headers: {resp.Headers}");
                    }

                    modCompatibilities = SimpleJson.DeserializeObject<List<ModCompatibility>>(resp.Content);
                    var cacheRoot = new CacheRoot
                    {
                        CachedDate = resp.Headers
                            .FirstOrDefault(header => header.Name.EqualsIgnoreCase("Last-Modified"))
                            ?.Value?.ToString(),
                        CachedETag = resp.Headers
                            .FirstOrDefault(header => header.Name.EqualsIgnoreCase("ETag"))
                            ?.Value?.ToString(),
                        Mods = modCompatibilities
                    };
                    _ = Task.Run(async () => await TrySaveCachedDb(cacheRoot));
                }

                Log.Message($"MP: successfully fetched {modCompatibilities.Count} mods compatibility info " +
                            $"in {stopwatch.Elapsed}");

                SetupFrom(modCompatibilities);
                fetchSuccess = true;
            }
            catch (Exception e)
            {
                Log.Warning($"MP: updating mod compatibility list failed:\n{e}");
                fetchSuccess = false;
            }
        }

        private static void SetupFrom(List<ModCompatibility> mods)
        {
            workshopLookup = mods
                .Where(mod => mod.workshopId != 0)
                .GroupBy(mod => mod.workshopId)
                .ToDictionary(grouping => grouping.Key, grouping => grouping.First());

            nameLookup = mods
                .GroupBy(mod => mod.name.ToLower())
                .ToDictionary(grouping => grouping.Key, grouping => grouping.First());
        }

        public static ModCompatibility LookupByWorkshopId(PublishedFileId_t workshopId) {
            return LookupByWorkshopId(workshopId.m_PublishedFileId);
        }

        public static ModCompatibility LookupByWorkshopId(ulong workshopId) {
            return workshopLookup.TryGetValue((long) workshopId);
        }

        public static ModCompatibility LookupByName(string name) {
            return nameLookup.TryGetValue(name.ToLower());
        }
    }

    public class ModCompatibility
    {
        // These are properties because RestSharp requires that for deserialization
        public int status { get; set; }
        public string name { get; set; }
        public long workshopId { get; set; }
        public string notes { get; set; } = "";
    }
}
