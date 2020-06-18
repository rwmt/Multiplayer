using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RestSharp;
using Steamworks;
using Verse;

namespace Multiplayer.Client
{
    public static class ModCompatibilityManager
    {
        private static bool startedLazyFetch;

        private static Dictionary<long, ModCompatibility> workshopLookup = new Dictionary<long, ModCompatibility>();
        private static Dictionary<string, ModCompatibility> nameLookup = new Dictionary<string, ModCompatibility>();

        private static void UpdateModCompatibilityDb() {
            startedLazyFetch = true;

            Task.Run(() => {
                var client = new RestClient("https://bot.rimworldmultiplayer.com/mod-compatibility?version=1.1&format=metadata");
                try {
                    var rawResponse = client.Get(new RestRequest($"", DataFormat.Json));
                    var modCompatibilities = SimpleJson.DeserializeObject<List<ModCompatibility>>(rawResponse.Content);
                    Log.Message($"MP: successfully fetched {modCompatibilities.Count} mods compatibility info");

                    workshopLookup = modCompatibilities
                        .GroupBy(mod => mod.workshopId)
                        .ToDictionary(grouping => grouping.Key, grouping => grouping.First());
                    workshopLookup.Remove(0);
                    nameLookup = modCompatibilities
                        .GroupBy(mod => mod.name.ToLower())
                        .ToDictionary(grouping => grouping.Key, grouping => grouping.First());
                }
                catch (Exception e) {
                    Log.Warning($"MP: updating mod compatibility list failed {e.Message} {e.StackTrace}");
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
        public int status;
        public string name;
        public long workshopId;
        public string notes = "";
    }
}
