using HarmonyLib;
using Multiplayer.Common;
using Multiplayer.Client;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using System.IO;

namespace Multiplayer.Client.EarlyPatches
{
    [HarmonyPatch]
    static class CaptureThingSetMakers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(ThingSetMaker_MarketValue).GetConstructor(new Type[0]);
            yield return typeof(ThingSetMaker_Nutrition).GetConstructor(new Type[0]);
        }

        public static List<ThingSetMaker> captured = new List<ThingSetMaker>();

        static void Prefix(ThingSetMaker __instance)
        {
            if (Current.ProgramState == ProgramState.Entry)
                captured.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(DirectXmlLoader), nameof(DirectXmlLoader.XmlAssetsInModFolder))]
    static class XmlAssetsInModFolderPatch
    {
        // Sorts the files before processing, ensures cross os compatibility
        static IEnumerable<LoadableXmlAsset> Postfix(IEnumerable<LoadableXmlAsset> __result)
        {
            var array = __result.ToArray();

            Array.Sort(array, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));

            return array;
        }
    }

    //[HarmonyPatch(typeof(LoadableXmlAsset), MethodType.Constructor)]
    //[HarmonyPatch(new[] { typeof(string), typeof(string), typeof(string) })]
    [HarmonyPatch(typeof(DirectXmlLoader), nameof(DirectXmlLoader.XmlAssetsInModFolder))]
    static class LoadableXmlAssetCtorPatch
    {
        public static ConcurrentDictionary<string, ConcurrentSet<ModFile>> xmlAssetHashes = new ConcurrentDictionary<string, ConcurrentSet<ModFile>>();

        static void Postfix(LoadableXmlAsset[] __result)
        {
            if (Multiplayer.hasLoaded) return;

            foreach (var asset in __result)
            {
                var fullPath = asset.FullFilePath.NormalizePath();
                var modDir = asset.mod.RootDir.NormalizePath();

                if (asset.mod != null && fullPath.StartsWith(modDir))
                {
                    var modId = asset.mod.PackageIdPlayerFacing.ToLowerInvariant();
                    
                    xmlAssetHashes
                    .GetOrAdd(modId, s => new ConcurrentSet<ModFile>())
                    .Add(new ModFile(fullPath, fullPath.IgnorePrefix(modDir), new FileInfo(fullPath).CRC32()));
                }
            }
        }

        /*public static int AggregateHash(ModContentPack mod)
        {
            return xmlAssetHashes.OrderBy(a => a.Item1, StringComparer.Ordinal).Where(p => p.First.mod == mod).Select(p => p.Second).AggregateHash();
        }*/

        public static void ClearHashBag()
        {
            //xmlAssetHashes = new ConcurrentBag<(string, int)>();
        }
    }

    [HarmonyPatch(typeof(PlayDataLoader), nameof(PlayDataLoader.ClearAllPlayData))]
    static class LoadableXmlAssetClearPatch
    {
        static void Prefix()
        {
            //LoadableXmlAssetCtorPatch.ClearHashBag();
        }
    }

    [HarmonyPatch]
    static class MonoIOPatch
    {
        public static ConcurrentSet<string> openedFiles = new ConcurrentSet<string>();

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.TypeByName("System.IO.MonoIO").GetMethod("Open");
        }

        static void Prefix(string filename)
        {
            if (!Multiplayer.hasLoaded)
                openedFiles.Add(filename.NormalizePath());
        }
    }

    [HarmonyPatch(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssemblyInt))]
    static class GenTypesOptimization
    {
        private static Dictionary<string, Type> RWAndSystemTypes = new();

        static GenTypesOptimization()
        {
            foreach (var type in typeof(Game).Assembly.GetTypes())
            {
                if (type.IsPublic)
                    RWAndSystemTypes[type.Name] = type;
            }
        }

        static bool Prefix(string typeName, ref Type __result)
        {
            if (!typeName.Contains(".") && RWAndSystemTypes.TryGetValue(typeName, out var type))
            {
                __result = type;
                return false;
            }

            return true;
        }
    }

#if DEBUG
    [HarmonyPatch(typeof(GlobalTextureAtlasManager), nameof(GlobalTextureAtlasManager.BakeStaticAtlases))]
    static class NoAtlases
    {
        static bool Prefix() => false;
    }
#endif
}
