using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer
{
    public static class BetterSaver
    {
        public static bool doBetterSave;
        public static List<Thing> loadedThings;
        private static Dictionary<ushort, ThingDef> thingDefsByShortHash;

        private static FieldInfo plantUnlitTicksField = AccessTools.Field(typeof(Plant), "unlitTicks");
        private static FieldInfo plantMadeLeaflessTickField = AccessTools.Field(typeof(Plant), "madeLeaflessTick");
        private static FieldInfo filthGrowTickField = AccessTools.Field(typeof(Filth), "growTick");
        public static FieldInfo compressorMapField = AccessTools.Field(typeof(MapFileCompressor), "map");

        public static void Save(Map map)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return;

            map.compressor.compressibilityDecider = new CompressibilityDecider(map);

            SaveRock(map);
            SavePlants(map);
            SaveRockRubble(map);
        }

        private static void SaveRock(Map map)
        {
            SaveCellData(map, (writer, cell) =>
            {
                Thing thing = null;

                foreach (Thing t in map.thingGrid.ThingsAt(cell))
                {
                    if (!IsSaveRock(t)) continue;
                    thing = t;
                    break;
                }

                if (thing != null)
                {
                    writer.Write(thing.def.shortHash);
                    writer.Write(thing.thingIDNumber);
                }
                else
                {
                    writer.Write((ushort)0);
                }
            }, "compressedRocks");
        }

        private static void SaveRockRubble(Map map)
        {
            SaveCellData(map, (writer, cell) =>
            {
                Filth thing = null;

                foreach (Thing t in map.thingGrid.ThingsAt(cell))
                {
                    if (!IsSaveRockRubble(t)) continue;
                    thing = t as Filth;
                    break;
                }

                if (thing != null)
                {
                    writer.Write(thing.def.shortHash);
                    writer.Write(thing.thingIDNumber);
                    writer.Write((byte)thing.thickness);
                    writer.Write((int)filthGrowTickField.GetValue(thing));
                }
                else
                {
                    writer.Write((ushort)0);
                }
            }, "compressedRockRubble");
        }

        private static void SavePlants(Map map)
        {
            SaveCellData(map, (writer, cell) =>
            {
                Plant thing = null;

                foreach (Thing t in map.thingGrid.ThingsAt(cell))
                {
                    if (!IsSavePlant(t)) continue;
                    thing = t as Plant;
                    break;
                }

                if (thing != null)
                {
                    writer.Write(thing.def.shortHash);
                    writer.Write(thing.thingIDNumber);
                    writer.Write(thing.HitPoints);

                    int sown = thing.sown ? 128 : 0;
                    int growth = (int)(Mathf.Clamp01(thing.Growth) * 127);
                    byte growthAndSown = (byte)(growth | sown);
                    writer.Write(growthAndSown);

                    writer.Write(thing.Age);
                    writer.Write((int)plantUnlitTicksField.GetValue(thing));
                    writer.Write((int)plantMadeLeaflessTickField.GetValue(thing));
                }
                else
                {
                    writer.Write((ushort)0);
                }
            }, "compressedPlants");
        }

        public static void Load(Map map)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;

            loadedThings = new List<Thing>();
            thingDefsByShortHash = new Dictionary<ushort, ThingDef>();

            foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs)
                thingDefsByShortHash[thingDef.shortHash] = thingDef;

            LoadRock(map);
            LoadPlants(map);
            LoadRockRubble(map);
        }

        private static void LoadRock(Map map)
        {
            LoadCellData(map, (reader, cell) =>
            {
                ushort defId = reader.ReadUInt16();
                if (defId == 0)
                    return;

                int id = reader.ReadInt32();
                ThingDef def = thingDefsByShortHash[defId];

                Thing thing = (Thing)Activator.CreateInstance(def.thingClass);
                thing.def = def;
                thing.HitPoints = thing.MaxHitPoints;

                thing.thingIDNumber = id;
                thing.SetPositionDirect(cell);
                loadedThings.Add(thing);
            }, "compressedRocks");
        }

        private static void LoadRockRubble(Map map)
        {
            LoadCellData(map, (reader, cell) =>
            {
                ushort defId = reader.ReadUInt16();
                if (defId == 0)
                    return;

                int id = reader.ReadInt32();
                byte thickness = reader.ReadByte();
                int growTick = reader.ReadInt32();
                ThingDef def = thingDefsByShortHash[defId];

                Filth thing = (Filth)Activator.CreateInstance(def.thingClass);
                thing.def = def;

                thing.thingIDNumber = id;
                thing.thickness = thickness;
                filthGrowTickField.SetValue(thing, growTick);

                thing.SetPositionDirect(cell);
                loadedThings.Add(thing);
            }, "compressedRockRubble");
        }

        private static void LoadPlants(Map map)
        {
            LoadCellData(map, (reader, cell) =>
            {
                ushort defId = reader.ReadUInt16();
                if (defId == 0)
                    return;

                int id = reader.ReadInt32();
                int hitPoints = reader.ReadInt32();
                byte growthAndSown = reader.ReadByte();
                int age = reader.ReadInt32();
                int plantUnlitTicks = reader.ReadInt32();
                int plantMadeLeaflessTick = reader.ReadInt32();

                float growth = (growthAndSown & 127) / 127f;
                bool sown = (growthAndSown & 128) > 0;

                ThingDef def = thingDefsByShortHash[defId];

                Plant thing = (Plant)Activator.CreateInstance(def.thingClass);
                thing.def = def;
                thing.thingIDNumber = id;
                thing.HitPoints = hitPoints;

                thing.InitializeComps();

                thing.Growth = growth;
                thing.Age = age;
                plantUnlitTicksField.SetValue(thing, plantUnlitTicks);
                plantMadeLeaflessTickField.SetValue(thing, plantMadeLeaflessTick);
                thing.sown = sown;

                thing.SetPositionDirect(cell);
                loadedThings.Add(thing);
            }, "compressedPlants");
        }

        public static bool IsSaveRock(Thing t)
        {
            return t.def.saveCompressible && (!t.def.useHitPoints || t.HitPoints == t.MaxHitPoints);
        }

        public static readonly HashSet<string> savePlants = new HashSet<string>() { "PlantGrass", "PlantTallGrass", "PlantTreeOak", "PlantTreePoplar", "PlantTreeBirch", "PlantTreePine", "PlantBush", "PlantBrambles", "PlantDandelion", "PlantRaspberry", "PlantMoss", "PlantSaguaroCactus", "PlantShrubLow", "PlantTreeWillow", "PlantTreeCypress", "PlantTreeMaple", "PlantChokevine", "PlantWildHealroot" };
        public static bool IsSavePlant(Thing t)
        {
            return savePlants.Contains(t.def.defName);
        }

        public static bool IsSaveRockRubble(Thing t)
        {
            return t.def == ThingDefOf.RockRubble;
        }

        private static void SaveCellData(Map map, Action<BinaryWriter, IntVec3> action, string label)
        {
            int cells = map.info.NumCells;

            using (BinaryWriter writer = new BinaryWriter(new MemoryStream()))
            {
                for (int i = 0; i < cells; i++)
                {
                    IntVec3 cell = map.cellIndices.IndexToCell(i);
                    action(writer, cell);
                }

                byte[] arr = (writer.BaseStream as MemoryStream).ToArray();
                DataExposeUtility.ByteArray(ref arr, label);
            }
        }

        private static void LoadCellData(Map map, Action<BinaryReader, IntVec3> action, string label)
        {
            int cells = map.info.NumCells;
            byte[] arr = null;
            DataExposeUtility.ByteArray(ref arr, label);

            using (BinaryReader reader = new BinaryReader(new MemoryStream(arr)))
            {
                for (int i = 0; i < cells; i++)
                {
                    IntVec3 cell = map.cellIndices.IndexToCell(i);
                    action(reader, cell);
                }
            }
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch(nameof(MapFileCompressor.BuildCompressedString))]
    public static class SaveCompressPatch
    {
        static bool Prefix(MapFileCompressor __instance)
        {
            if (!BetterSaver.doBetterSave) return true;

            Map map = (Map)BetterSaver.compressorMapField.GetValue(__instance);
            BetterSaver.Save(map);

            return false;
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch(nameof(MapFileCompressor.ExposeData))]
    public static class SaveDecompressPatch
    {
        static bool Prefix(MapFileCompressor __instance)
        {
            if (!BetterSaver.doBetterSave) return true;

            Map map = (Map)BetterSaver.compressorMapField.GetValue(__instance);
            BetterSaver.Load(map);

            return false;
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch(nameof(MapFileCompressor.ThingsToSpawnAfterLoad))]
    public static class DecompressedThingsPatch
    {
        static void Postfix(ref IEnumerable<Thing> __result)
        {
            if (!BetterSaver.doBetterSave) return;

            __result = BetterSaver.loadedThings;
            BetterSaver.loadedThings = null;
        }
    }

    [HarmonyPatch(typeof(CompressibilityDeciderUtility))]
    [HarmonyPatch(nameof(CompressibilityDeciderUtility.IsSaveCompressible))]
    public static class SaveCompressiblePatch
    {
        static void Postfix(Thing t, ref bool __result)
        {
            if (!BetterSaver.doBetterSave) return;

            __result = BetterSaver.IsSavePlant(t) || BetterSaver.IsSaveRock(t) || BetterSaver.IsSaveRockRubble(t);
        }
    }
}
