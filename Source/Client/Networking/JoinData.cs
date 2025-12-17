using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Ionic.Zlib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using Verse;

namespace Multiplayer.Client
{

    public static class JoinData
    {
        public static List<ModMetaData> activeModsSnapshot;
        public static ModFileDict modFilesSnapshot;

        public static byte[] WriteServerData(bool writeConfigs)
        {
            var data = new ByteWriter();

            data.WriteInt32(activeModsSnapshot.Count);
            foreach (var m in activeModsSnapshot)
            {
                data.WriteString(m.PackageIdNonUnique);
                data.WriteString(m.Name);
                data.WriteULong((ulong)m.GetPublishedFileId());
                data.WriteEnum(m.Source);
            }

            data.WriteInt32(modFilesSnapshot.Count());
            foreach (var files in modFilesSnapshot)
            {
                data.WriteString(files.Key);
                data.WriteInt32(files.Value.Count);

                foreach (var file in files.Value.Values)
                {
                    data.WriteString(file.relPath);
                    data.WriteInt32(file.hash);
                }
            }

            data.WriteBool(writeConfigs);
            if (writeConfigs)
            {
                var configs = SyncConfigs.GetSyncableConfigContents(
                    activeModsSnapshot.Select(m => m.PackageIdNonUnique).ToList()
                );

                data.WriteInt32(configs.Count);
                foreach (var config in configs)
                {
                    data.WriteString(config.ModId);
                    data.WriteString(config.FileName);
                    data.WriteString(config.Contents);
                }
            }

            return GZipStream.CompressBuffer(data.ToArray());
        }

        public static void ReadServerData(byte[] compressedData, RemoteData remoteInfo)
        {
            var data = new ByteReader(GZipStream.UncompressBuffer(compressedData));

            var modCount = data.ReadInt32();
            for (int i = 0; i < modCount; i++)
            {
                var packageId = data.ReadString();
                var name = data.ReadString();
                var steamId = data.ReadULong();
                var source = data.ReadEnum<ContentSource>();

                remoteInfo.remoteMods.Add(new ModInfo() { packageId = packageId, name = name, steamId = steamId, source = source });
            }

            var rootCount = data.ReadInt32();
            for (int i = 0; i < rootCount; i++)
            {
                var modId = data.ReadString();
                var mod = GetInstalledMod(modId);
                var fileCount = data.ReadInt32();

                for (int j = 0; j < fileCount; j++)
                {
                    var relPath = data.ReadString();
                    var hash = data.ReadInt32();
                    string absPath = null;

                    if (mod != null)
                        absPath = Path.Combine(mod.RootDir.FullName, relPath);

                    remoteInfo.remoteFiles.Add(modId, new ModFile(absPath, relPath, hash));
                }
            }

            remoteInfo.hasConfigs = data.ReadBool();
            if (remoteInfo.hasConfigs)
            {
                var configCount = data.ReadInt32();
                for (int i = 0; i < configCount; i++)
                {
                    const int MaxConfigContentLen = 8388608; // 8 megabytes

                    var modId = data.ReadString();
                    var fileName = data.ReadString();
                    var contents = data.ReadString(MaxConfigContentLen);

                    remoteInfo.remoteModConfigs.Add(new ModConfig(modId, fileName, contents));
                    //remoteInfo.remoteModConfigs[trimmedPath] = remoteInfo.remoteModConfigs[trimmedPath].Insert(0, "a"); // for testing
                }
            }
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
                    fileDict.Add(modId, new ModFile(asm.FullName, relPath, asm.CRC32()));
                }

                foreach (var xmlFile in GetModDefsAndPatches(contentPack))
                {
                    var relPath = xmlFile.FullName.RemovePrefix(contentPack.RootDir).NormalizePath();
                    fileDict.Add(modId, new ModFile(xmlFile.FullName, relPath, xmlFile.CRC32()));
                }
            }

            return fileDict;
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

        public string remoteAddress;
        public int remotePort;
        public CSteamID? remoteSteamHost;

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

    public struct ModFile
    {
        public string absPath; // Can be null on the remote side
        public string relPath;
        public int hash;

        public ModFile(string absPath, string relPath, int hash)
        {
            this.absPath = absPath?.NormalizePath();
            this.relPath = relPath.NormalizePath();
            this.hash = hash;
        }

        public bool Equals(ModFile other)
        {
            return relPath == other.relPath && hash == other.hash;
        }

        public override bool Equals(object obj)
        {
            return obj is ModFile other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Gen.HashCombineInt(relPath.GetHashCode(), hash);
        }
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
