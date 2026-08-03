using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Steamworks;
using Verse;

namespace Multiplayer.Client
{

    public static class JoinData
    {
        public static List<ModMetaData> activeModsSnapshot;
        public static ModFileDict modFilesSnapshot;

        public static List<ClientInitDataPacket.ModData> WriteServerData(bool writeConfigs)
        {
            var configs = writeConfigs ? SyncConfigs.GetSyncableConfigContents(
                activeModsSnapshot.Select(m => m.PackageIdNonUnique).ToList()
            ) : [];
            return activeModsSnapshot.Select(meta =>
            {
                var files = modFilesSnapshot
                    .Where(kv => kv.Key == meta.PackageIdNonUnique)
                    .SelectMany(modIdToFilesPair => modIdToFilesPair.Value)
                    .Select(pathToFilePair => new ClientInitDataPacket.ModFile
                    {
                        path = pathToFilePair.Value.relPath,
                        hash = pathToFilePair.Value.hash,
                        assemblyVersion = pathToFilePair.Value.assemblyVersion ?? "",
                        fileVersion = pathToFilePair.Value.fileVersion ?? "",
                        productVersion = pathToFilePair.Value.productVersion ?? "",
                        writeTime = pathToFilePair.Value.writeTime,
                    })
                    .ToList();

                var localConfig = configs.FirstOrDefault(localConfig => localConfig.ModId == meta.PackageIdNonUnique);
                var source = meta.Source switch
                {
                    ContentSource.Undefined => ClientInitDataPacket.ModSource.Undefined,
                    ContentSource.OfficialModsFolder => ClientInitDataPacket.ModSource.OfficialModsFolder,
                    ContentSource.ModsFolder => ClientInitDataPacket.ModSource.ModsFolder,
                    ContentSource.SteamWorkshop => ClientInitDataPacket.ModSource.SteamWorkshop,
                    _ => throw new ArgumentOutOfRangeException()
                };

                return new ClientInitDataPacket.ModData
                {
                    packageIdNonUnique = meta.PackageIdNonUnique,
                    name = meta.Name,
                    publishedFileId = (ulong)meta.GetPublishedFileId(),
                    source = source,
                    files = files,
                    config = localConfig == null
                        ? null
                        : new ClientInitDataPacket.ModConfig
                        {
                            fileName = localConfig.FileName,
                            contents = localConfig.Contents,
                        }
                };
            }).ToList();
        }

        public static ModMetaData GetInstalledMod(string id)
        {
            if (ModsConfig.IsActive(id + ModMetaData.SteamModPostfix))
                id += ModMetaData.SteamModPostfix;

            return ModLister.GetModWithIdentifier(id);
        }

        public static bool CompareToLocal(RemoteData remote)
        {
            return
                remote.remoteRwVersion == VersionControl.CurrentVersionString &&
                remote.CompareMods(activeModsSnapshot) == ModListDiff.None &&
                remote.remoteFiles.DictsEqual(modFilesSnapshot) &&
                (!remote.hasConfigs || remote.remoteModConfigs.EqualAsSets(SyncConfigs.GetSyncableConfigContents(remote.RemoteModIds.ToList())));
        }

        internal static void TakeModDataSnapshot()
        {
            activeModsSnapshot = ModsConfig.ActiveModsInLoadOrder.ToList();
            modFilesSnapshot = GetModFiles(activeModsSnapshot.Select(m => m.PackageIdNonUnique));
        }

        internal static ModFileDict GetModFiles(IEnumerable<string> modIds)
        {
            var fileDict = new ModFileDict();

            foreach (var modId in modIds)
            {
                var mod = GetInstalledMod(modId);
                if (mod == null || !mod.RootDir.Exists) continue;

                var contentPack = MultiplayerData.DummyContentPack(mod);

                foreach (var asm in MultiplayerData.GetModAssemblies(contentPack))
                {
                    var relPath = asm.FullName.RemovePrefix(contentPack.RootDir).NormalizePath();
                    var (assemblyVersion, fileVersion, productVersion) = ReadDllVersions(asm.FullName);
                    fileDict.Add(modId, new ModFile(asm.FullName, relPath, asm.CRC32(),
                        assemblyVersion, fileVersion, productVersion, asm.LastWriteTimeUtc.Ticks));
                }

                foreach (var xmlFile in GetModDefsAndPatches(contentPack))
                {
                    var relPath = xmlFile.FullName.RemovePrefix(contentPack.RootDir).NormalizePath();
                    fileDict.Add(modId, new ModFile(xmlFile.FullName, relPath, xmlFile.CRC32(),
                        writeTime: xmlFile.LastWriteTimeUtc.Ticks));
                }
            }

            return fileDict;
        }

        // Reads the assembly, file and product versions of a .dll. Most mods bump the file
        // version per release while leaving the assembly version at 1.0.0.0. Each is null when
        // absent (unset 0.0.0.0 or unreadable/native DLL).
        private static (string assembly, string file, string product) ReadDllVersions(string path)
        {
            string assembly = null, file = null, product = null;

            try
            {
                var v = AssemblyName.GetAssemblyName(path).Version;
                if (v != null && v != new System.Version(0, 0, 0, 0))
                    assembly = v.ToString();
            }
            catch { }

            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                var fv = new System.Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart);
                if (fv != new System.Version(0, 0, 0, 0))
                    file = fv.ToString();
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                    product = fvi.ProductVersion.Trim();
            }
            catch { }

            return (assembly, file, product);
        }

        public static IEnumerable<FileInfo> GetModDefsAndPatches(ModContentPack mod)
        {
            foreach (var f in ModContentPack.GetAllFilesForModPreserveOrder(mod, "Defs/", f => f.ToLower() == ".xml"))
                yield return f.Item2;

            foreach (var f in ModContentPack.GetAllFilesForModPreserveOrder(mod, "Patches/", f => f.ToLower() == ".xml"))
                yield return f.Item2;
        }

        private static Dictionary<string, bool> modInstalled = new();

        public static bool IsModInstalledCached(string packageId)
        {
            if (modInstalled.TryGetValue(packageId, out var result))
                return result;

            return modInstalled[packageId] =
                GetInstalledMod(packageId) is { } m && (!m.OnSteamWorkshop || SteamModInstalled(m));
        }

        public static void ClearInstalledCache()
        {
            modInstalled.Clear();
        }

        private static bool SteamModInstalled(ModMetaData mod) =>
            ((EItemState)SteamUGC.GetItemState(mod.GetPublishedFileId()))
            .HasFlag(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateSubscribed);
    }

    public class RemoteData
    {
        public string remoteRwVersion;
        public string remoteMpVersion;

        public List<ModInfo> remoteMods = new();
        public ModFileDict remoteFiles = new();
        public List<ModConfig> remoteModConfigs = new();
        public bool hasConfigs;

        public IEnumerable<string> RemoteModIds => remoteMods.Select(m => m.packageId);

        public ModListDiff CompareMods(List<ModMetaData> localMods)
        {
            var mods1 = remoteMods.Select(m => (m.packageId, m.source));
            var mods2 = localMods.Select(m => (m.PackageIdNonUnique, m.Source));

            if (!mods1.EqualAsSets(mods2))
                return ModListDiff.NoMatchAsSets;

            if (!RemoteModIds.SequenceEqual(localMods.Select(m => m.PackageIdNonUnique)))
                return ModListDiff.WrongOrder;

            return ModListDiff.None;
        }

        public static RemoteData FromNet(ServerJoinDataPacket packet)
        {
            var remoteInfo = new RemoteData
            {
                remoteRwVersion = packet.rwVersion,
                remoteMpVersion = packet.mpVersion,
                hasConfigs = packet.configsIncluded,
            };

            foreach (var mod in packet.ServerInitData)
            {
                var modInfo = new ModInfo
                {
                    packageId = mod.packageIdNonUnique, name = mod.name, steamId = mod.publishedFileId,
                    source = mod.source switch
                    {
                        ClientInitDataPacket.ModSource.Undefined => ContentSource.Undefined,
                        ClientInitDataPacket.ModSource.OfficialModsFolder => ContentSource.OfficialModsFolder,
                        ClientInitDataPacket.ModSource.ModsFolder => ContentSource.ModsFolder,
                        ClientInitDataPacket.ModSource.SteamWorkshop => ContentSource.SteamWorkshop,
                        _ => throw new ArgumentOutOfRangeException()
                    }
                };
                remoteInfo.remoteMods.Add(modInfo);

                var modMeta = JoinData.GetInstalledMod(modInfo.packageId);
                static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
                foreach (var modFile in mod.files)
                {
                    var absPath = modMeta == null ? null : Path.Combine(modMeta.RootDir.FullName, modFile.path);
                    remoteInfo.remoteFiles.Add(modInfo.packageId, new ModFile(absPath, modFile.path, modFile.hash,
                        NullIfEmpty(modFile.assemblyVersion), NullIfEmpty(modFile.fileVersion),
                        NullIfEmpty(modFile.productVersion), modFile.writeTime));
                }

                if (mod.config.HasValue)
                {
                    var modConfig = mod.config.Value;
                    remoteInfo.remoteModConfigs.Add(
                        new ModConfig(modInfo.packageId, modConfig.fileName, modConfig.contents));
                }
            }

            return remoteInfo;
        }
    }

    public enum ModListDiff
    {
        None, NoMatchAsSets, WrongOrder
    }

    public record ModConfig(string ModId, string FileName, string Contents);

    public class ModFileDict : IEnumerable<KeyValuePair<string, Dictionary<string, ModFile>>>
    {
        // Mod id => (path => file)
        private Dictionary<string, Dictionary<string, ModFile>> files = new();

        public void Add(string mod, ModFile file){
            files.GetOrAddNew(mod)[file.relPath] = file;
        }

        public ModFile? GetOrDefault(string mod, string relPath){
            if (files.TryGetValue(mod, out var modFiles) && modFiles.TryGetValue(relPath, out var file))
                return file;
            return null;
        }

        public bool DictsEqual(ModFileDict other)
        {
            return files.Keys.EqualAsSets(other.files.Keys) &&
                   files.All(kv => kv.Value.Values.EqualAsSets(other.files[kv.Key].Values));
        }

        public IEnumerator<KeyValuePair<string, Dictionary<string, ModFile>>> GetEnumerator()
        {
            return files.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return files.GetEnumerator();
        }

        public ModFileDict CopyWithMods(IEnumerable<string> modIds)
        {
            var dict = new ModFileDict();

            foreach (var modId in modIds)
                if (files.TryGetValue(modId, out var ofMod))
                    dict.files[modId] = ofMod;

            return dict;
        }
    }

    public struct ModInfo
    {
        public string packageId; // Mod package id, lower case, no _steam suffix
        public string name;
        public ulong steamId; // Zero means invalid
        public ContentSource source;

        public bool Installed => JoinData.IsModInstalledCached(packageId);

        public bool CanSubscribe => steamId != 0;
    }

    public struct ModFile(string absPath, string relPath, int hash,
        string assemblyVersion = null, string fileVersion = null, string productVersion = null, long writeTime = 0)
    {
        public string absPath = absPath?.NormalizePath(); // Can be null on the remote side
        public string relPath = relPath.NormalizePath();
        public int hash = hash;
        public string assemblyVersion = assemblyVersion; // .dll only, null otherwise
        public string fileVersion = fileVersion;         // .dll only, null otherwise
        public string productVersion = productVersion;   // .dll only, null otherwise
        public long writeTime = writeTime; // Last write time, UTC ticks (0 if unknown)

        // Equality stays on relPath + hash only: version/write time are display metadata and
        // must not affect the file diff (content changes are already reflected by the hash).
        public bool Equals(ModFile other) =>
            relPath == other.relPath && hash == other.hash;

        public override bool Equals(object obj) =>
            obj is ModFile other && Equals(other);

        public override int GetHashCode() =>
            Gen.HashCombineInt(relPath.GetHashCode(), hash);
    }

    [HarmonyPatch(typeof(ModLister), nameof(ModLister.RebuildModList))]
    static class ClearCacheRebuildModList
    {
        static void Prefix()
        {
            JoinData.ClearInstalledCache();
        }
    }
}
