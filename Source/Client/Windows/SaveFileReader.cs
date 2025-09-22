#nullable enable
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class SaveFileReader
    {
        public List<FileInfo> SpSaves { get; private set; } = [];
        public List<FileInfo> MpSaves { get; private set; } = [];

        private ConcurrentDictionary<FileInfo, SaveFile> data = new();
        private Task? spTask, mpTask;

        public void StartReading()
        {
            SpSaves = GenFilePaths.AllSavedGameFiles.OrderByDescending(f => f.LastWriteTime).ToList();

            var replaysDir = new DirectoryInfo(Multiplayer.ReplaysDir);
            var toRead = replaysDir.GetFiles("*.zip", MpVersion.IsDebug ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            MpSaves = toRead.OrderByDescending(f => f.LastWriteTime).ToList();

            spTask = Task.Run(() => SpSaves.ForEach(ReadSpSave));

            // Loading Replays currently uses DirectXmlToObject which isn't thread-safe.
            // Running the whole task on one thread is fine, though it can't be parallelized any further
            mpTask = Task.Run(() => MpSaves.ForEach(ReadMpSave));
        }

        public void WaitTasks()
        {
            spTask?.Wait();
            mpTask?.Wait();
        }

        private void ReadSpSave(FileInfo file)
        {
            try
            {
                data[file] = SaveFile.ReadSpSave(file);
            }
            catch (Exception ex)
            {
                Log.Warning($"Exception loading save info of {file.Name}: {ex}");
            }
        }

        private void ReadMpSave(FileInfo file)
        {
            try
            {
                var saveFile = SaveFile.ReadMpSave(file);
                if (saveFile != null) data[file] = saveFile;
            }
            catch (Exception ex)
            {
                Log.Warning($"Exception loading replay info of {file.Name}: {ex}");
            }
        }

        public SaveFile? GetData(FileInfo file)
        {
            data.TryGetValue(file, out var saveFile);
            return saveFile;
        }

        public void RemoveFile(FileInfo info)
        {
            SpSaves.Remove(info);
            MpSaves.Remove(info);
        }
    }

    public class SaveFile(string displayName, bool replay, FileInfo file)
    {
        public string displayName = displayName;
        public bool replay = replay;
        public int replaySections;
        public FileInfo file = file;

        public string? gameName;

        public string? rwVersion;
        public string[] modNames = [];
        public string[] modIds = [];

        public int protocol;
        public bool asyncTime;
        public bool multifaction;

        public bool HasRwVersion => rwVersion != null;

        public bool MajorAndMinorVerEqualToCurrent => rwVersion != null &&
            VersionControl.MajorFromVersionString(rwVersion) == VersionControl.CurrentMajor &&
            VersionControl.MinorFromVersionString(rwVersion) == VersionControl.CurrentMinor;

        public Color VersionColor
        {
            get
            {
                if (rwVersion == null)
                    return Color.red;

                if (MajorAndMinorVerEqualToCurrent)
                    return new Color(0.6f, 0.6f, 0.6f);

                if (BackCompatibility.IsSaveCompatibleWith(rwVersion))
                    return Color.yellow;

                return Color.red;
            }
        }

        public static SaveFile ReadSpSave(FileInfo file)
        {
            var saveFile = new SaveFile(Path.GetFileNameWithoutExtension(file.Name), false, file);
            using var stream = file.OpenRead();
            ReadSaveInfo(stream, saveFile);
            return saveFile;
        }

        public static SaveFile? ReadMpSave(FileInfo file)
        {
            var displayName = Path.ChangeExtension(Path.GetRelativePath(Multiplayer.ReplaysDir, file.FullName), null);
            var saveFile = new SaveFile(displayName, true, file);

            var replay = Replay.ForLoading(file);
            if (!replay.LoadInfo()) return null;

            saveFile.gameName = replay.info.name;
            saveFile.protocol = replay.info.protocol;
            saveFile.replaySections = replay.info.sections.Count;

            if (!replay.info.rwVersion.NullOrEmpty())
            {
                saveFile.rwVersion = replay.info.rwVersion;
                saveFile.modIds = replay.info.modIds.ToArray();
                saveFile.modNames = replay.info.modNames.ToArray();
                saveFile.asyncTime = replay.info.asyncTime;
                saveFile.multifaction = replay.info.multifaction;
            }
            else
            {
                using var zip = replay.OpenZipRead();
                using var stream = zip.GetEntry("world/000_save")!.Open();
                ReadSaveInfo(stream, saveFile);
            }

            return saveFile;
        }

        private static void ReadSaveInfo(Stream stream, SaveFile save)
        {
            using var reader = new XmlTextReader(stream);
            reader.ReadToNextElement(); // savedGame
            reader.ReadToNextElement(); // meta

            if (reader.Name != "meta") return;

            reader.ReadToDescendant("gameVersion");
            save.rwVersion = VersionControl.VersionStringWithoutRev(reader.ReadString());

            reader.ReadToNextSibling("modIds");
            save.modIds = reader.ReadStrings();

            reader.ReadToNextSibling("modNames");
            save.modNames = reader.ReadStrings();
        }
    }
}
