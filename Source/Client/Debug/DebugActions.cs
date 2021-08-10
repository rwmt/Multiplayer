using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

using HarmonyLib;
using Multiplayer.API;

using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    static class MPDebugActions
    {
        const string MultiplayerCategory = "Multiplayer";

        [DebugAction("General", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnShuttleAcceptColonists()
        {
            var shuttle = ThingMaker.MakeThing(ThingDefOf.Shuttle, null);
            shuttle.TryGetComp<CompShuttle>().acceptColonists = true;
            GenPlace.TryPlaceThing(shuttle, UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
        }

        [SyncMethod]
        [DebugAction(MultiplayerCategory, "Save Map", allowedGameStates = AllowedGameStates.Playing)]
        public static void SaveGameCmd()
        {
            Map map = Find.CurrentMap;
            byte[] mapData = ScribeUtil.WriteExposable(Current.Game, "map", true);
            File.WriteAllBytes($"map_{map.uniqueID}_{Multiplayer.username}.xml", mapData);
        }

        [DebugAction(MultiplayerCategory, "Save Map (local)", allowedGameStates = AllowedGameStates.Playing)]
        public static void SaveGameCmdLocal()
        {
            Map map = Find.CurrentMap;
            byte[] mapData = ScribeUtil.WriteExposable(Current.Game, "map", true);
            File.WriteAllBytes($"map_{map.uniqueID}_{Multiplayer.username}.xml", mapData);
        }

        [SyncMethod]
        [DebugAction(MultiplayerCategory, "Save Game", allowedGameStates = AllowedGameStates.Playing)]
        public static void SaveGame()
        {
            Game game = Current.Game;
            byte[] data = ScribeUtil.WriteExposable(game, "game", true);
            File.WriteAllBytes($"game_{Multiplayer.username}.xml", data);
        }

        [DebugAction(MultiplayerCategory, "Save Game (local)", allowedGameStates = AllowedGameStates.Playing)]
        public static void SaveGameLocal()
        {
            Game game = Current.Game;
            byte[] data = ScribeUtil.WriteExposable(game, "game", true);
            File.WriteAllBytes($"game_{Multiplayer.username}.xml", data);
        }

        [DebugAction(MultiplayerCategory, "Dump Sync Types", allowedGameStates = AllowedGameStates.Entry)]
        public static void DumpSyncTypes()
        {
            var dict = new Dictionary<string, Type[]>() {
                {"ThingComp", SyncSerialization.thingCompTypes},
                {"AbilityComp", SyncSerialization.abilityCompTypes},
                {"Designator", SyncSerialization.designatorTypes},
                {"WorldObjectComp", SyncSerialization.worldObjectCompTypes},
                {"IStoreSettingsParent", SyncSerialization.storageParents},
                {"IPlantToGrowSettable", SyncSerialization.plantToGrowSettables},

                {"GameComponent", SyncSerialization.gameCompTypes},
                {"WorldComponent", SyncSerialization.worldCompTypes},
                {"MapComponent", SyncSerialization.mapCompTypes},
            };
            foreach(var kv in dict) {
                Log.Warning($"== {kv.Key} ==");
                Log.Message(
                    kv.Value
                    .Select(type => $"{type.Name}")
                    .Join(delimiter: "\n")
                );
            }
        }

        [DebugAction(MultiplayerCategory, "Dump Def Types", allowedGameStates = AllowedGameStates.Entry)]
        public static void DumpDefTypes()
        {
            foreach (var defType in GenTypes.AllLeafSubclasses(typeof(Def)))
            {
                if (defType.Assembly != typeof(Game).Assembly) continue;
                if (MultiplayerData.IgnoredVanillaDefTypes.Contains(defType)) continue;

                Log.Warning($"== {defType.Name} ==");
                Log.Message(
                    GenDefDatabase.GetAllDefsInDatabaseForDef(defType)
                    .Select(def => $"{def.defName}")
                    .Join(delimiter: "\n")
                );
            }
        }

#if DEBUG

        [DebugOutput]
        public unsafe static void PrintInlined()
        {
            foreach (var m in Harmony.GetAllPatchedMethods())
            {
                // method->inline_info
                if ((*(byte*)(m.MethodHandle.Value + 32) & 1) == 1)
                    Log.Warning($"Mono inlined {m.FullDescription()} {*(byte*)(m.MethodHandle.Value + 32) & 1} {*((ushort*)(m.MethodHandle.Value) + 1) & (ushort)MethodImplOptions.NoInlining}");
            }
        }

        [DebugAction(DebugActionCategories.Mods, "Log Terrain", allowedGameStates = AllowedGameStates.Entry)]
        public static void EntryAction()
        {
            Log.Message(
                GenDefDatabase.GetAllDefsInDatabaseForDef(typeof(TerrainDef))
                .Select(def => $"{def.modContentPack?.Name} {def} {def.shortHash} {def.index}")
                .Join(delimiter: "\n")
            );
        }

        [DebugAction(DebugActionCategories.Mods, "Print static fields (game)", allowedGameStates = AllowedGameStates.Entry)]
        public static void PrintStaticFields()
        {
            Log.Message(StaticFieldsToString(typeof(Game).Assembly, type => type.Namespace.StartsWith("RimWorld") || type.Namespace.StartsWith("Verse")));
        }

        [DebugAction(DebugActionCategories.Mods, "Print static fields (mods)", allowedGameStates = AllowedGameStates.Entry)]
        public static void AllModStatics()
        {
            var builder = new StringBuilder();

            foreach (var mod in LoadedModManager.RunningModsListForReading) {
                builder.AppendLine("======== ").Append(mod.Name).AppendLine();
                foreach (var asm in mod.assemblies.loadedAssemblies) {
                    builder.AppendLine(StaticFieldsToString(asm, t => !t.Namespace.StartsWith("Harmony") && !t.Namespace.StartsWith("Multiplayer")));
                }
            }

            Log.Message(builder.ToString(), true);
        }

        static string StaticFieldsToString(Assembly asm, Predicate<Type> typeValidator)
        {
            var builder = new StringBuilder();

            object FieldValue(FieldInfo field)
            {
                var value = field.GetValue(null);
                if (value is System.Collections.ICollection col)
                    return col.Count;
                if (field.Name.ToLowerInvariant().Contains("path") && value is string path && (path.Contains("/") || path.Contains("\\")))
                    return "[x]";
                return value;
            }

            foreach (var type in asm.GetTypes())
                if (!type.IsGenericTypeDefinition && type.Namespace != null && typeValidator(type) && !type.HasAttribute<DefOf>() && !type.HasAttribute<CompilerGeneratedAttribute>())
                    foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                        if (!field.IsLiteral && !field.IsInitOnly && !field.HasAttribute<CompilerGeneratedAttribute>())
                            builder.AppendLine($"{field.FieldType} {type}::{field.Name}: {FieldValue(field)}");

            return builder.ToString();
        }

        [DebugAction(MultiplayerCategory, "Print Patched Hashes", allowedGameStates = AllowedGameStates.Entry)]
        public static void SaveHashes()
        {
            var builder = new StringBuilder();

            // We only care about transpiled methods that aren't part of MP.
            var query = Multiplayer.harmony.GetPatchedMethods()
                .Where(m => !m.DeclaringType.Namespace.StartsWith("Multiplayer") &&
                    !Harmony.GetPatchInfo(m).Transpilers.NullOrEmpty());
                
            foreach (var method in query) {
                builder.Append(GetMethodHash(method));
                builder.Append(" ");
                builder.Append(method.DeclaringType.FullName);
                builder.Append(":");
                builder.Append(method.Name);
                builder.AppendLine();
            }

            Log.Message(builder.ToString());
        }

        static string GetMethodHash(MethodBase method)
        {
            var toHash = new List<byte>();
            foreach(var ins in PatchProcessor.GetOriginalInstructions(method)) {
                toHash.Add(Encoding.UTF8.GetBytes(ins.opcode.Name + ins.operand));
            }

            using (var sha256 = System.Security.Cryptography.SHA256.Create()) {
                return Convert.ToBase64String(sha256.ComputeHash(toHash.ToArray()));
            }
        }
#endif

    }
}
