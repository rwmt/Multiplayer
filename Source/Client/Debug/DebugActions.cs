using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

using HarmonyLib;
using LudeonTK;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Debug = UnityEngine.Debug;

namespace Multiplayer.Client
{
    static class MpDebugActions
    {
        const string MultiplayerCategory = "Multiplayer";

        [DebugAction(MultiplayerCategory, actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void SpawnCaravans()
        {
            for (int a = 0; a < 10; a++)
            {
                int num = GenWorld.MouseTile();
                if (Find.WorldGrid[num].biome.impassable)
                {
                    return;
                }

                List<Pawn> list = new List<Pawn>();
                int num2 = Rand.RangeInclusive(1, 10);
                for (int i = 0; i < num2; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(Faction.OfPlayer.def.basicMemberKind, Faction.OfPlayer);
                    list.Add(pawn);
                    if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        ThingDef thingDef = DefDatabase<ThingDef>.AllDefs.Where((ThingDef def) =>
                                def.IsWeapon && !def.weaponTags.NullOrEmpty() &&
                                (def.weaponTags.Contains("SimpleGun") ||
                                 def.weaponTags.Contains(
                                     "IndustrialGunAdvanced") ||
                                 def.weaponTags.Contains("SpacerGun") ||
                                 def.weaponTags.Contains(
                                     "MedievalMeleeAdvanced") ||
                                 def.weaponTags.Contains(
                                     "NeolithicRangedBasic") ||
                                 def.weaponTags.Contains(
                                     "NeolithicRangedDecent") ||
                                 def.weaponTags.Contains(
                                     "NeolithicRangedHeavy")))
                            .RandomElementWithFallback();
                        pawn.equipment.AddEquipment(
                            (ThingWithComps)ThingMaker.MakeThing(thingDef, GenStuff.RandomStuffFor(thingDef)));
                    }
                }

                int num3 = Rand.RangeInclusive(-4, 10);
                for (int j = 0; j < num3; j++)
                {
                    Pawn item = PawnGenerator.GeneratePawn(
                        DefDatabase<PawnKindDef>.AllDefs
                            .Where((PawnKindDef d) => d.RaceProps.Animal && d.race.GetStatValueAbstract(StatDefOf.Wildness) < 1f).RandomElement(),
                        Faction.OfPlayer);
                    list.Add(item);
                }

                Caravan caravan =
                    CaravanMaker.MakeCaravan(list, Faction.OfPlayer, num, addToWorldPawnsIfNotAlready: true);

                List<Thing> list2 = ThingSetMakerDefOf.DebugCaravanInventory.root.Generate();
                for (int k = 0; k < list2.Count; k++)
                {
                    Thing thing = list2[k];
                    if (!(thing.GetStatValue(StatDefOf.Mass) * (float)thing.stackCount >
                          caravan.MassCapacity - caravan.MassUsage))
                    {
                        CaravanInventoryUtility.GiveThing(caravan, thing);
                        continue;
                    }

                    break;
                }
            }
        }

        [DebugAction(MultiplayerCategory, "Set faction (rect)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 100)]
        private static void SetFaction()
        {
        	DebugToolsGeneral.GenericRectTool("Set faction (rect)", rect =>
        	{
                List<FloatMenuOption> factionOptions = new List<FloatMenuOption>();
                foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
                {
                    FloatMenuOption item = new FloatMenuOption(faction.Name, () =>
                    {
                        foreach (Thing thing in rect.SelectMany(c => Find.CurrentMap.thingGrid.ThingsAt(c)))
                        {
                            if (thing.def.CanHaveFaction)
                                thing.SetFaction(faction);

                            if (thing is IThingHolder holder && holder.GetDirectlyHeldThings() != null)
                                foreach (var heldThing in holder.GetDirectlyHeldThings())
                                    if (heldThing.def.CanHaveFaction)
                                        heldThing.SetFaction(faction);
                        }
                    });
                    factionOptions.Add(item);
                }
                Find.WindowStack.Add(new FloatMenu(factionOptions));
        	});
        }

        [DebugAction(MultiplayerCategory, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnShuttleAcceptColonists()
        {
            var shuttle = ThingMaker.MakeThing(ThingDefOf.Shuttle);
            shuttle.TryGetComp<CompShuttle>().acceptColonists = true;
            GenPlace.TryPlaceThing(shuttle, UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
        }

        [DebugAction(MultiplayerCategory, "Save Game", allowedGameStates = AllowedGameStates.Playing)]
        public static void SaveGame()
        {
            Game game = Current.Game;
            byte[] data = ScribeUtil.WriteExposable(game, "game", true);
            File.WriteAllBytes($"game_{Multiplayer.username}.xml", data);
        }

        [DebugAction(MultiplayerCategory, "Dump Sync Types", allowedGameStates = AllowedGameStates.Entry)]
        public static void DumpSyncTypes()
        {
            var dict = new Dictionary<string, Type[]>() {
                {"ThingComp", CompSerialization.thingCompTypes},
                {"AbilityComp", CompSerialization.abilityCompTypes},
                {"WorldObjectComp", CompSerialization.worldObjectCompTypes},
                {"HediffComp", CompSerialization.hediffCompTypes},

                {"GameComponent", CompSerialization.gameCompTypes},
                {"WorldComponent", CompSerialization.worldCompTypes},
                {"MapComponent", CompSerialization.mapCompTypes},
            };

            foreach (var syncWithImplType in Multiplayer.serialization.syncWithImplTypes)
                dict[syncWithImplType.Name] = Multiplayer.serialization.TypeHelper!.GetImplementations(syncWithImplType).ToArray();

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

        [DebugAction(MultiplayerCategory, "Dump IRenameable Types", allowedGameStates = AllowedGameStates.Entry)]
        static void DumpIRenameableTypes()
        {
            var synced = new List<Type>();
            var unsynced = new List<Type>();
            var methods = typeof(IRenameable).AllImplementing()
                .Select(t => AccessTools.DeclaredPropertySetter(t, nameof(IRenameable.RenamableLabel)))
                .AllNotNull();

            foreach (var method in methods)
            {
                // Check if a method is synced or not
                if (Sync.methodBaseToInternalId.ContainsKey(method))
                    synced.Add(method.DeclaringType);
                else
                    unsynced.Add(method.DeclaringType);
            }

            Log.Warning("== Synced IRenameable types ==");
            Log.Message(!synced.Any() ? "No types" : synced.Select(GetNameWithNamespace).Join(delimiter: "\n"));

            Log.Warning("== Unsynced IRenameable types ==");
            Log.Message(!unsynced.Any() ? "No types" : unsynced.Select(GetNameWithNamespace).Join(delimiter: "\n"));

            static string GetNameWithNamespace(Type t) => t.Namespace.NullOrEmpty() ? t.Name : $"{t.Namespace}.{t.Name}";
        }

        [DebugAction(MultiplayerCategory, allowedGameStates = AllowedGameStates.Playing)]
        static void LogAllPatch()
        {
            foreach (var method in Assembly.GetExecutingAssembly().DefinedTypes.SelectMany(t => t.DeclaredMethods))
                if (method.Name != "MultiplayerMethodCallLogger" &&
                    !method.Name.StartsWith("get_") &&
                    !method.IsGenericMethod &&
                    method.DeclaringType?.IsGenericType is false &&
                    method.DeclaringType?.BaseType != typeof(MulticastDelegate) &&
                    !method.IsAbstract)
                    Multiplayer.harmony.Patch(
                        method,
                        prefix: new HarmonyMethod(typeof(MpDebugActions), nameof(MultiplayerMethodCallLogger))
                    );
        }

        [DebugAction(MultiplayerCategory, allowedGameStates = AllowedGameStates.Entry)]
        static void LogAllPatchEntry()
        {
            LogAllPatch();
        }

        static void MultiplayerMethodCallLogger(MethodBase __originalMethod)
        {
            Debug.Log(__originalMethod.FullDescription());
        }

#if DEBUG

        [DebugOutput]
        public static unsafe void PrintInlined()
        {
            foreach (var m in Harmony.GetAllPatchedMethods())
            {
                // method->inline_info
                if ((*(byte*)(m.MethodHandle.Value + 32) & 1) == 1)
                    Log.Warning($"Mono inlined {m.FullDescription()} {*(byte*)(m.MethodHandle.Value + 32) & 1} {*((ushort*)(m.MethodHandle.Value) + 1) & (ushort)MethodImplOptions.NoInlining}");
            }
        }

        [DebugOutput(true)]
        public static void PrintThingListers()
        {
            var output = new StringBuilder();

            void PrintLister(ListerThings lister)
            {
                for (int i = 0; i < lister.listsByGroup.Length; i++)
                    if (lister.listsByGroup[i] != null)
                    {
                        output.Append((ThingRequestGroup)i).Append(" ");
                        foreach (var t in lister.listsByGroup[i])
                            output.Append(t).Append(",");
                        output.AppendLine();
                    }

                foreach (var kv in lister.listsByDef)
                {
                    output.Append(kv.Key).Append(" ");
                    foreach (var t in kv.Value)
                        output.Append(t).Append(",");
                    output.AppendLine();
                }
            }

            PrintLister(Find.CurrentMap.listerThings);

            foreach (var region in Find.CurrentMap.regionGrid.AllRegions)
            {
                output.AppendLine($"REGION {region.extentsClose} {region.AnyCell} {region.id}");
                PrintLister(region.ListerThings);
            }

            File.WriteAllText(Application.consoleLogPath + ".regioninfo", output.ToString());
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

            Log.Message(builder.ToString());
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
                .Where(m => !(m.DeclaringType.Namespace?.StartsWith("Multiplayer") ?? false)
                    && !Harmony.GetPatchInfo(m).Transpilers.NullOrEmpty());

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
