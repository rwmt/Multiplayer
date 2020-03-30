using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Steamworks;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    public static class ModManagement
    {
        public static List<ulong> GetEnabledWorkshopMods() {
            var enabledModIds = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToArray();
            var allWorkshopItems =
                WorkshopItems.AllSubscribedItems.Where<WorkshopItem>(
                    (Func<WorkshopItem, bool>) (it => it is WorkshopItem_Mod)
                );
            var workshopModIds = new List<ulong>();
            foreach (WorkshopItem workshopItem in allWorkshopItems) {
                ModMetaData mod = new ModMetaData(workshopItem);

                if (enabledModIds.Contains(mod.PackageIdNonUnique)) {
                    workshopModIds.Add(workshopItem.PublishedFileId.m_PublishedFileId);
                }
            }

            return workshopModIds;
        }

        public static void DownloadWorkshopMods(ulong[] workshopModIds) {
            try {
                var downloadInProgress = new List<PublishedFileId_t>();
                foreach (var workshopModId in workshopModIds) {
                    var publishedFileId = new PublishedFileId_t(workshopModId);
                    var itemState = (EItemState) SteamUGC.GetItemState(publishedFileId);
                    if (!itemState.HasFlag(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateSubscribed)) {
                        Log.Message($"Starting workshop download {publishedFileId}");
                        SteamUGC.SubscribeItem(publishedFileId);
                        downloadInProgress.Add(publishedFileId);
                    }
                }

                // wait for all workshop downloads to complete
                while (downloadInProgress.Count > 0) {
                    var publishedFileId = downloadInProgress.First();
                    var itemState = (EItemState) SteamUGC.GetItemState(publishedFileId);
                    if (itemState.HasFlag(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateSubscribed)) {
                        downloadInProgress.RemoveAt(0);
                    }
                    else {
                        Log.Message($"Waiting for workshop download {publishedFileId} status {itemState}");
                        Thread.Sleep(200);
                    }
                }
            }
            catch (InvalidOperationException e) {
                Log.Error($"MP Workshop mod sync error: {e.Message}");
            }
        }

        /// Calls the private <see cref="WorkshopItems.RebuildItemsList"/>) to manually detect newly downloaded Workshop mods
        public static void RebuildModsList() {
            // ReSharper disable once PossibleNullReferenceException
            typeof(WorkshopItems)
                .GetMethod("RebuildItemsList", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(obj: null, parameters: new object[] { });
        }
    }
}
