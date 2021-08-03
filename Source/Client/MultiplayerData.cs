using Multiplayer.Client.EarlyPatches;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    static class MultiplayerData
    {
        public static ModFileDict modFiles = new ModFileDict();

        internal static void CollectModFiles()
        {
            // todo check file path starts correctly, move to the long event?
            foreach (var mod in LoadedModManager.RunningModsListForReading.Select(m => (m, m.ModAssemblies())))
                foreach (var f in mod.Item2)
                {
                    var modId = mod.Item1.PackageIdPlayerFacing.ToLowerInvariant();
                    var relPath = f.FullName.IgnorePrefix(mod.Item1.RootDir.NormalizePath()).NormalizePath();

                    modFiles.Add(modId, new ModFile(f.FullName, relPath, f.CRC32()));
                }

            foreach (var kv in LoadableXmlAssetCtorPatch.xmlAssetHashes)
            {
                foreach (var f in kv.Value)
                    modFiles.Add(kv.Key, f);
            }
        }

        internal static void CacheXMLMods()
        {
            foreach (var mod in ModLister.AllInstalledMods)
            {
                if (!mod.ModHasAssemblies())
                    Multiplayer.xmlMods.Add(mod.RootDir.FullName);
            }
        }

        public static UniqueList<Texture2D> icons = new UniqueList<Texture2D>();
        public static UniqueList<IconInfo> iconInfos = new UniqueList<IconInfo>();

        public class IconInfo
        {
            public bool hasStuff;
        }

        internal static void CollectCursorIcons()
        {
            icons.Add(null);
            iconInfos.Add(null);

            foreach (var des in DefDatabase<DesignationCategoryDef>.AllDefsListForReading.SelectMany(c => c.AllResolvedDesignators))
            {
                if (des.icon == null) continue;

                if (icons.Add(des.icon))
                    iconInfos.Add(new IconInfo()
                    {
                        hasStuff = des is Designator_Build build && build.entDef.MadeFromStuff
                    });
            }
        }

        internal static HashSet<Type> IgnoredVanillaDefTypes = new HashSet<Type>
        {
            typeof(FeatureDef), typeof(HairDef),
            typeof(MainButtonDef), typeof(PawnTableDef),
            typeof(TransferableSorterDef), typeof(ConceptDef),
            typeof(InstructionDef), typeof(EffecterDef),
            typeof(ImpactSoundTypeDef), typeof(KeyBindingCategoryDef),
            typeof(KeyBindingDef), typeof(RulePackDef),
            typeof(ScatterableDef), typeof(ShaderTypeDef),
            typeof(SongDef), typeof(SoundDef),
            typeof(SubcameraDef), typeof(PawnColumnDef)
        };

        internal static void CollectDefInfos()
        {
            var dict = new Dictionary<string, DefInfo>();

            int TypeHash(Type type) => GenText.StableStringHash(type.FullName);

            dict["ThingComp"] = GetDefInfo(SyncSerialization.thingCompTypes, TypeHash);
            dict["AbilityComp"] = GetDefInfo(SyncSerialization.abilityCompTypes, TypeHash);
            dict["Designator"] = GetDefInfo(SyncSerialization.designatorTypes, TypeHash);
            dict["WorldObjectComp"] = GetDefInfo(SyncSerialization.worldObjectCompTypes, TypeHash);
            dict["IStoreSettingsParent"] = GetDefInfo(SyncSerialization.storageParents, TypeHash);
            dict["IPlantToGrowSettable"] = GetDefInfo(SyncSerialization.plantToGrowSettables, TypeHash);

            dict["GameComponent"] = GetDefInfo(SyncSerialization.gameCompTypes, TypeHash);
            dict["WorldComponent"] = GetDefInfo(SyncSerialization.worldCompTypes, TypeHash);
            dict["MapComponent"] = GetDefInfo(SyncSerialization.mapCompTypes, TypeHash);

            dict["PawnBio"] = GetDefInfo(SolidBioDatabase.allBios, b => b.name.GetHashCode());
            dict["Backstory"] = GetDefInfo(BackstoryDatabase.allBackstories.Keys, b => b.GetHashCode());

            foreach (var defType in GenTypes.AllLeafSubclasses(typeof(Def)))
            {
                if (defType.Assembly != typeof(Game).Assembly) continue;
                if (IgnoredVanillaDefTypes.Contains(defType)) continue;

                var defs = GenDefDatabase.GetAllDefsInDatabaseForDef(defType);
                dict.Add(defType.Name, GetDefInfo(defs, d => GenText.StableStringHash(d.defName)));
            }

            Multiplayer.localDefInfos = dict;
        }

        internal static DefInfo GetDefInfo<T>(IEnumerable<T> types, Func<T, int> hash)
        {
            return new DefInfo()
            {
                count = types.Count(),
                hash = types.Select(t => hash(t)).AggregateHash()
            };
        }

        internal static void CollectModHashes()
        {
            Multiplayer.enabledModAssemblyHashes.Clear();

            foreach (var mod in LoadedModManager.RunningModsListForReading)
            {
                var hashes = new ModHashes()
                {
                    assemblyHash = mod.ModAssemblies().CRC32(),
                    //xmlHash = LoadableXmlAssetCtorPatch.AggregateHash(mod),
                    aboutHash = new DirectoryInfo(Path.Combine(mod.RootDir, "About")).GetFiles().CRC32()
                };

                Multiplayer.enabledModAssemblyHashes.Add(hashes);
            }
        }
    }

    public class ModHashes
    {
        public int assemblyHash, xmlHash, aboutHash;
    }
}
