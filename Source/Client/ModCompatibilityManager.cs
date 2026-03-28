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
        private static bool startedLazyFetch;
        public static bool? fetchSuccess;

        private static Dictionary<long, ModCompatibility> workshopLookup = new();
        public static Dictionary<string, ModCompatibility> nameLookup = new();

        private const string CacheFileName = "compat.json";
        private static readonly string CacheFilePath = Path.Combine(Multiplayer.CacheDir, CacheFileName);
        private class CacheRoot
        {
            public string? CachedDate { get; set; }
            public string? CachedETag { get; set; }
            public List<ModCompatibility> Mods { get; set; }
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
        private static void ClearModCompatCache() => File.Delete(CacheFilePath);

        [DebugAction(category = "Multiplayer", allowedGameStates = AllowedGameStates.Entry)]
        private static void UpdateModCompatibilityDb() {
            startedLazyFetch = true;

            Task.Run(async () =>
            {
                var client = new RestClient("https://bot.rimworldmultiplayer.com/");
                client.AddDefaultHeader("X-Multiplayer-Version", MpVersion.Version);
                try {
                    var cached = await TryLoadCachedDb();
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
                    List<ModCompatibility> modCompatibilities;
                    if (resp.StatusCode == HttpStatusCode.NotModified)
                    {
                        modCompatibilities = cached!.Mods;
                    }
                    else
                    {
                        if (resp.StatusCode != HttpStatusCode.OK)
                        {
                            Log.Warning($"MP: received unexpected status code {resp.StatusCode} when fetching mod compatibility. Headers: {resp.Headers}");
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
                    var elapsed = stopwatch.Elapsed;
                    Log.Message($"MP: successfully fetched {modCompatibilities.Count} mods compatibility info in {elapsed}");

                    workshopLookup = modCompatibilities
                        .Where(mod => mod.workshopId != 0)
                        .GroupBy(mod => mod.workshopId)
                        .ToDictionary(grouping => grouping.Key, grouping => grouping.First());

                    nameLookup = modCompatibilities
                        .GroupBy(mod => mod.name.ToLower())
                        .ToDictionary(grouping => grouping.Key, grouping => grouping.First());

                    fetchSuccess = true;
                }
                catch (Exception e) {
                    Log.Warning($"MP: updating mod compatibility list failed {e.Message} {e.StackTrace}");
                    fetchSuccess = false;
                }
            });
        }

        public static ModCompatibility LookupByWorkshopId(PublishedFileId_t workshopId) {
            return LookupByWorkshopId(workshopId.m_PublishedFileId);
        }

        public static ModCompatibility LookupByWorkshopId(ulong workshopId) {
            if (!startedLazyFetch) {
                UpdateModCompatibilityDb();
            }

            return workshopLookup.TryGetValue((long) workshopId);
        }

        public static ModCompatibility LookupByName(string name) {
            if (!startedLazyFetch) {
                UpdateModCompatibilityDb();
            }

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
