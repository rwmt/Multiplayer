using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ionic.Zlib;
using Multiplayer.Client.EarlyPatches;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client
{
    public static class JoinData
    {
        public static byte[] WriteServerData()
        {
            var data = new ByteWriter();

            data.WriteInt32(ModsConfig.ActiveModsInLoadOrder.Count());
            foreach (var m in ModsConfig.ActiveModsInLoadOrder)
            {
                data.WriteString(m.PackageIdNonUnique);
                data.WriteString(m.Name);
                data.WriteULong((ulong)m.GetPublishedFileId());
                data.WriteByte((byte)m.Source);
            }

            data.WriteInt32(ModFiles.Count());
            foreach (var files in ModFiles)
            {
                data.WriteString(files.Key);
                data.WriteInt32(files.Value.Count);

                foreach (var file in files.Value.Values)
                {
                    data.WriteString(file.relPath);
                    data.WriteInt32(file.hash);
                }
            }

            data.WriteInt32(ModConfigPaths.Count());
            foreach (var config in ModConfigContents)
            {
                data.WriteString(config.Item1);
                data.WriteString(config.Item2);
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
                var source = (ContentSource)data.ReadByte();

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
                    var hash = data.ReadInt32() + 1; // todo for testing
                    string absPath = null;

                    if (mod != null)
                        absPath = Path.Combine(mod.RootDir.FullName, relPath);

                    remoteInfo.remoteFiles.Add(modId, new ModFile(absPath, relPath, hash));
                }
            }

            var configCount = data.ReadInt32();
            for (int i = 0; i < configCount; i++)
            {
                const int MaxConfigContentLen = 8388608; // 8MB

                var trimmedPath = data.ReadString();
                var contents = data.ReadString(MaxConfigContentLen);

                remoteInfo.remoteModConfigs[trimmedPath] = contents.Insert(0, "a"); // todo for testing
            }
        }

        public static ModMetaData GetInstalledMod(string id) => ModLister.GetModWithIdentifier(id, true);
        
        public static string NormalizedDataFolder => GenFilePaths.SaveDataFolderPath.NormalizePath();
        public static ModFileDict ModFiles => Multiplayer.modFiles;
        public static IEnumerable<string> ModConfigPaths => MonoIOPatch.openedFiles.Where(f => f.StartsWith(NormalizedDataFolder));
        public static IEnumerable<(string, string)> ModConfigContents => ModConfigPaths.Select(p => (p.IgnorePrefix(NormalizedDataFolder), File.ReadAllText(p)));

        public static bool DataEqual(RemoteData remote)
        {
            return 
                remote.remoteMods.Select(m => (m.packageId, m.source)).SequenceEqual(ModsConfig.ActiveModsInLoadOrder.Select(m => (m.PackageIdNonUnique, m.Source))) &&
                remote.remoteFiles.Equals(ModFiles) &&
                remote.remoteModConfigs.Select(p => (p.Key, p.Value)).EqualAsSets(JoinData.ModConfigContents);
        }
    }

    public class RemoteData
    {
        public string remoteRwVersion;

        public string[] remoteModNames;
        public string[] remoteModIds;
        public ulong[] remoteWorkshopModIds;

        public List<ModInfo> remoteMods = new List<ModInfo>();
        public ModFileDict remoteFiles = new ModFileDict();
        public Dictionary<string, string> remoteModConfigs = new Dictionary<string, string>();
        public Dictionary<string, DefInfo> defInfo = new Dictionary<string, DefInfo>();

        public string remoteAddress;
        public int remotePort;
    }

    public class ModFileDict : IEnumerable<KeyValuePair<string, Dictionary<string, ModFile>>>
    {
        private Dictionary<string, Dictionary<string, ModFile>> files = new Dictionary<string, Dictionary<string, ModFile>>();

        public void Add(string mod, ModFile file){
            files.GetOrAddNew(mod)[file.relPath] = file;
        }

        public ModFile? GetOrDefault(string mod, string relPath){
            if (files.TryGetValue(mod, out var modFiles) && modFiles.TryGetValue(relPath, out var file))
                return file;
            return null;
        }

        public bool Equals(ModFileDict other){
            return files.Keys.EqualAsSets(other.files.Keys) &&
                files.All(kv => kv.Value.Values.EqualAsSets(kv.Value.Values));
        }

        public IEnumerator<KeyValuePair<string, Dictionary<string, ModFile>>> GetEnumerator()
        {
            return files.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return files.GetEnumerator();
        }
    }

    public struct ModInfo
    {
        public string packageId; // Mod package id with no _steam suffix
        public string name;
        public ulong steamId;
        public ContentSource source;

        public bool Installed => ModLister.GetModWithIdentifier(packageId, true) != null;
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
    }
}
