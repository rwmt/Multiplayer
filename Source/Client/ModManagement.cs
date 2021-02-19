using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Steamworks;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    public static class ModManagement
    {
        private static Regex NewLinePattern = new Regex(@"\r\n?|\n");
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

        public static bool ModsMatch(IEnumerable<string> remoteModIds) {
            var localModIds = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToList();
            var set = remoteModIds.ToHashSet();
            set.SymmetricExceptWith(localModIds);
            return !set.Any();
        }

        public static IEnumerable<string> GetNotInstalledMods(IEnumerable<string> remoteModIds) {
            return remoteModIds.Except(ModLister.AllInstalledMods.Select(modInfo => modInfo.PackageId));
        }

        /// <exception cref="InvalidOperationException">Thrown if Steamworks fails (eg. when running non-steam)</exception>
        public static void DownloadWorkshopMods(ulong[] workshopModIds) {
            var missingWorkshopModIds = workshopModIds.Where(workshopModId
                => ModLister.AllInstalledMods.All(modMetaData => modMetaData.GetPublishedFileId().m_PublishedFileId != workshopModId)
            ).ToList();

            var downloadInProgress = new List<PublishedFileId_t>();
            foreach (var workshopModId in missingWorkshopModIds) {
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

        /// Calls the private <see cref="WorkshopItems.RebuildItemsList"/>) to manually detect newly downloaded Workshop mods
        public static void RebuildModsList() {
            // ReSharper disable once PossibleNullReferenceException
            typeof(WorkshopItems)
                .GetMethod("RebuildItemsList", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(obj: null, parameters: new object[] { });
        }

        public static void ApplyHostModConfigFiles(Dictionary<string, string> hostModConfigFiles)
        {
            if (hostModConfigFiles == null) {
                Log.Warning("MP: hostModConfigFiles is null");
                return;
            }
            if (CheckModConfigsMatch(hostModConfigFiles)) {
                return;
            }

            var localConfigsBackupDir = GenFilePaths.FolderUnderSaveData($"LocalConfigsBackup-{DateTime.Now:yyyy-MM-ddTHH-mm-ss}");
            (new DirectoryInfo(Path.Combine(localConfigsBackupDir, "Config"))).Create();

            foreach (var modConfigData in hostModConfigFiles) {
                var relativeFilePath = modConfigData.Key.Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(GenFilePaths.SaveDataFolderPath, relativeFilePath);
                if (File.Exists(filePath)) {
                    var backupFilePath = Path.Combine(localConfigsBackupDir, relativeFilePath);
                    Directory.GetParent(backupFilePath).Create();
                    File.Move(filePath, backupFilePath);
                }
                else {
                    Directory.GetParent(filePath).Create();
                }
                File.WriteAllText(filePath, modConfigData.Value);
            }

            File.Copy(
                Path.Combine(GenFilePaths.SaveDataFolderPath, "Config", "ModsConfig.xml"),
                Path.Combine(localConfigsBackupDir, "Config", "ModsConfig.xml")
            );
        }

        public static void RestoreConfigBackup(DirectoryInfo backupDir)
        {
            var backupFiles = backupDir.GetFiles("*.xml", SearchOption.AllDirectories);

            foreach (var backupFile in backupFiles) {
                var relativePath = backupFile.FullName.Substring(backupDir.FullName.Length + 1);
                var actualFilePath = Path.Combine(GenFilePaths.SaveDataFolderPath, relativePath);
                if (File.Exists(actualFilePath)) {
                    File.Delete(actualFilePath);
                }
                else {
                    Directory.GetParent(actualFilePath).Create();
                }
                File.Move(backupFile.FullName, actualFilePath);
            }

            backupDir.Delete(true);
        }

        public static bool HasRecentConfigBackup()
        {
            return new DirectoryInfo(GenFilePaths.SaveDataFolderPath)
                .GetDirectories("LocalConfigsBackup-*", SearchOption.TopDirectoryOnly)
                .Any(dir => dir.CreationTime.AddDays(-14) <= DateTime.Now);
        }

        public static DirectoryInfo GetMostRecentConfigBackup()
        {
            return new DirectoryInfo(GenFilePaths.SaveDataFolderPath)
                .GetDirectories("LocalConfigsBackup-*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(dir => dir.CreationTime)
                .First();
        }

        public static List<string> GetMismatchedModConfigs(Dictionary<string, string> hostFiles)
        {
            var mismatchedFiles = new List<string>();
            if (MultiplayerMod.arbiterInstance) {
                return mismatchedFiles;
            }
            foreach (var hostModEntry in hostFiles) {
                var hostFileName = hostModEntry.Key;
                var localFileName = Path.Combine(
                    GenFilePaths.SaveDataFolderPath,
                    hostFileName.Replace('/', Path.DirectorySeparatorChar)
                );
                if (!File.Exists(localFileName)) {
                    mismatchedFiles.Add(hostFileName);
                    continue;
                }

                var localFileContents = NewLinePattern.Replace(File.ReadAllText(localFileName), "\n");
                if (hostModEntry.Value != localFileContents) {
                    mismatchedFiles.Add(hostFileName);
                    continue;
                }
            }

            return mismatchedFiles;
        }

        public static bool CheckModConfigsMatch(Dictionary<string, string> hostFiles)
        {
            if (GetMismatchedModConfigs(hostFiles).Any()) {
                return false;
            }

            return true;
        }

        public static Dictionary<string, string> GetSyncableConfigFiles()
        {
            return new DirectoryInfo(GenFilePaths.ConfigFolderPath)
                .GetFiles("*.xml", SearchOption.AllDirectories)
                .Concat(new DirectoryInfo(GenFilePaths.FolderUnderSaveData("HugsLib")).GetFiles("*.xml",
                    SearchOption.AllDirectories))
                .Where(FilterNonSyncedConfigFiles)
                .ToDictionary(
                    file => ModManagement.ModConfigFileRelative(file.FullName, true),
                    file => NewLinePattern.Replace(file.OpenText().ReadToEnd(), "\n")
                );
        }

        public static bool FilterNonSyncedConfigFiles(FileInfo file)
        {
            return file.Name != "KeyPrefs.xml"
                   && file.Name != "Knowledge.xml"
                   && file.Name != "ModsConfig.xml" // already synced at earlier step
                   && file.Name != "Prefs.xml" // base game stuff: volume, resolution, etc
                   && file.Name != "LastSeenNews.xml"
                   && file.Name != "ColourPicker.xml"
                   && !file.Name.EndsWith("MultiplayerMod.xml") // contains username
                   && !file.Name.EndsWith("TwitchToolkit.xml") // contains username
                   && !file.Name.EndsWith("DubsMintMinimapMod.xml")
                   && !file.Name.EndsWith("DubsMintMenusMod.xml")
                   && !file.Name.EndsWith("TacticalGroupsMod.xml")
                   && !file.Name.EndsWith("RimThemes.xml")
                   && !file.Name.EndsWith("CameraPlusMain.xml")
                   && !file.Name.EndsWith("GraphicSetter.xml")
                   && !file.Name.EndsWith("Moody.xml")
                   && !file.Name.EndsWith("ModManager.xml")
                   && !file.Name.EndsWith("ModSwitch.xml")
                   && !file.Name.EndsWith("RimConnection.xml") // contains secret key for streamer
                   && file.Name.IndexOf("backup", StringComparison.OrdinalIgnoreCase) == -1
                   && file.DirectoryName.IndexOf("backup", StringComparison.OrdinalIgnoreCase) == -1
                   && !file.DirectoryName.Contains("RimHUD");
        }

        private static string ModConfigFileRelative(string modConfigFile, bool normalizeSeparators)
        {
            // trim up to base savedata dir, eg. Config/Mod_7535268789_SettingController.xml
            var relative = modConfigFile.Substring(Path.GetFullPath(GenFilePaths.SaveDataFolderPath).Length + 1);
            return normalizeSeparators
                ? relative.Replace('\\', '/') // normalize directory separator to /
                : relative.Replace('/', Path.DirectorySeparatorChar);
        }

        /// Extension of <see cref="ModsConfig.RestartFromChangedMods"/>) with -connect
        public static void PromptRestartAndReconnect(string address, int port)
        {
            // todo: clear -connect after launching? hmm, or patch ModsConfig.Restart to exclude it?
            // todo: if launched normally, prompt to return to backed up configs (and enabled mods?)
            Find.WindowStack.Add((Window) new Dialog_MessageBox("ModsChanged".Translate(), (string) null,
                (Action) (() => {
                    string[] commandLineArgs = Environment.GetCommandLineArgs();
                    string processFilename = commandLineArgs[0];
                    string arguments = "";
                    for (int index = 1; index < commandLineArgs.Length; ++index) {
                        if (string.Compare(commandLineArgs[index], "-connect", true) == 0) {
                            continue; // skip any existing -connect command
                        }
                        if (!arguments.NullOrEmpty()) {
                            arguments += " ";
                        }
                        arguments += "\"" + commandLineArgs[index].Replace("\"", "\\\"") + "\"";
                    }
                    if (address != null) {
                        arguments += $" -connect={address}:{port}";
                    }

                    new Process()
                    {
                        StartInfo = new ProcessStartInfo(processFilename, arguments)
                    }.Start();
                    Root.Shutdown();
                    LongEventHandler.QueueLongEvent((Action) (() => Thread.Sleep(10000)), "Restarting", true, (Action<Exception>) null, true);
                }), (string) null, (Action) null, (string) null, false, (Action) null, (Action) null));
        }

        public static void PromptRestart()
        {
            PromptRestartAndReconnect(null, 0);
        }
    }
}
