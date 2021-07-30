using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class SharedCrossRefs : LoadedObjectDirectory
    {
        // Used in CrossRefs patches
        public HashSet<string> tempKeys = new HashSet<string>();

        public void Unregister(ILoadReferenceable reffable)
        {
            allObjectsByLoadID.Remove(reffable.GetUniqueLoadID());
        }

        public void UnregisterAllTemp()
        {
            foreach (var key in tempKeys)
                allObjectsByLoadID.Remove(key);

            tempKeys.Clear();
        }

        public void UnregisterAllFrom(Map map)
        {
            foreach (var val in allObjectsByLoadID.Values.ToArray())
            {
                if (val is Thing thing && thing.Map == map ||
                    val is PassingShip ship && ship.Map == map ||
                    val is Bill bill && bill.Map == map
                )
                    Unregister(val);
            }
        }
    }

    public static class ThingsById
    {
        public static Dictionary<int, Thing> thingsById = new Dictionary<int, Thing>();

        public static void Register(Thing t)
        {
            thingsById[t.thingIDNumber] = t;
        }

        public static void Unregister(Thing t)
        {
            thingsById.Remove(t.thingIDNumber);
        }

        public static void UnregisterAllFrom(Map map)
        {
            thingsById.RemoveAll((id, thing) => thing.Map == map);
        }
    }

    public static class ScribeUtil
    {
        private const string RootNode = "root";

        private static MemoryStream stream;

        public static SharedCrossRefs sharedCrossRefs => Multiplayer.game.sharedCrossRefs;
        public static LoadedObjectDirectory defaultCrossRefs;

        public static bool loading;

        static string filename;

        public static void StartWriting(bool indent = false)
        {
            stream = new MemoryStream();

            Scribe.mode = LoadSaveMode.Saving;
            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings()
            {
                Indent = indent,
                OmitXmlDeclaration = true
            };

            XmlWriter writer = XmlWriter.Create(stream, xmlWriterSettings);
            Scribe.saver.writer = writer;
            writer.WriteStartDocument();
        }

        public static byte[] FinishWriting()
        {
            Scribe.saver.FinalizeSaving();

            byte[] arr = stream.ToArray();
            stream.Close();
            stream = null;

            return arr;
        }

        public static void StartWritingToDoc()
        {
            Scribe.mode = LoadSaveMode.Saving;
            XmlWriter xmlWriter = new CustomXmlWriter();
            Scribe.saver.writer = xmlWriter;
            xmlWriter.WriteStartDocument();
        }

        public static XmlDocument FinishWritingToDoc()
        {
            var doc = (Scribe.saver.writer as CustomXmlWriter).doc;
            Scribe.saver.FinalizeSaving();
            return doc;
        }

        public static void StartLoading(XmlDocument doc)
        {
            loading = true;

            ScribeMetaHeaderUtility.loadedGameVersion = VersionControl.CurrentVersionStringWithRev;

            Scribe.loader.curXmlParent = doc.DocumentElement;
            Scribe.mode = LoadSaveMode.LoadingVars;
        }

        public static void StartLoading(byte[] data)
        {
            StartLoading(LoadDocument(data));
        }

        public static void FinalizeLoading()
        {
            if (!loading)
            {
                Log.Error("Called FinalizeLoading() but we aren't loading");
                return;
            }

            ScribeLoader loader = Scribe.loader;

            try
            {
                Scribe.ExitNode();

                loader.curXmlParent = null;
                loader.curParent = null;
                loader.curPathRelToParent = null;
                loader.crossRefs.ResolveAllCrossReferences();
                loader.initer.DoAllPostLoadInits();
            }
            catch (Exception e)
            {
                Log.Error("Exception in FinalizeLoading(): " + e);
                loader.ForceStop();
                throw;
            }
            finally
            {
                loading = false;
            }
        }

        public static XmlDocument LoadDocument(byte[] data)
        {
            using var stream = new MemoryStream(data);
            using var reader = XmlReader.Create(stream);

            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(reader);

            return xmlDocument;
        }

        public static byte[] XmlToByteArray(XmlNode node, string rootNode = null, bool indent = false)
        {
            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings()
            {
                Indent = indent,
                OmitXmlDeclaration = true
            };

            using (MemoryStream stream = new MemoryStream())
            using (XmlWriter writer = XmlWriter.Create(stream, xmlWriterSettings))
            {
                if (rootNode != null)
                    writer.WriteStartElement(rootNode);

                node.WriteTo(writer);

                if (rootNode != null)
                    writer.WriteEndElement();

                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void SupplyCrossRefs()
        {
            if (sharedCrossRefs == null) return;

            if (!loading)
            {
                Log.Warning("Tried to supply cross refs without calling ScribeUtil.StartLoading()");
                return;
            }

            if (defaultCrossRefs == null)
                defaultCrossRefs = Scribe.loader.crossRefs.loadedObjectDirectory;

            Scribe.loader.crossRefs.loadedObjectDirectory = sharedCrossRefs;

            Log.Message($"Cross ref supply: {sharedCrossRefs.allObjectsByLoadID.Count} {sharedCrossRefs.allObjectsByLoadID.LastOrDefault()} {Faction.OfPlayer}");
        }

        public static byte[] WriteExposable(IExposable element, string name = RootNode, bool indent = false, Action beforeElement = null)
        {
            StartWriting(indent);
            Scribe.EnterNode(RootNode);
            beforeElement?.Invoke();
            Scribe_Deep.Look(ref element, name);
            return FinishWriting();
        }

        public static T ReadExposable<T>(byte[] data, Action<T> beforeFinish = null) where T : IExposable
        {
            StartLoading(data);
            SupplyCrossRefs();
            T element = default(T);
            Scribe_Deep.Look(ref element, RootNode);

            beforeFinish?.Invoke(element);

            FinalizeLoading();

            // Default cross refs restored in LoadedObjectsClearPatch

            return element;
        }

        // Copy of RimWorld's method but with ctor args
        public static void LookValueDeep<K, V>(ref Dictionary<K, V> dict, string label, params object[] valueCtorArgs)
        {
            List<K> keysWorkingList = null;
            List<V> valuesWorkingList = null;

            if (Scribe.EnterNode(label))
            {
                try
                {
                    if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        keysWorkingList = new List<K>();
                        valuesWorkingList = new List<V>();
                    }

                    if (Scribe.mode == LoadSaveMode.Saving)
                    {
                        foreach (KeyValuePair<K, V> current in dict)
                        {
                            keysWorkingList.Add(current.Key);
                            valuesWorkingList.Add(current.Value);
                        }
                    }

                    Scribe_Collections.Look(ref keysWorkingList, "keys", LookMode.Value);
                    Scribe_Collections.Look(ref valuesWorkingList, "values", LookMode.Deep, valueCtorArgs);

                    if (Scribe.mode == LoadSaveMode.Saving)
                    {
                        if (keysWorkingList != null)
                        {
                            keysWorkingList.Clear();
                            keysWorkingList = null;
                        }

                        if (valuesWorkingList != null)
                        {
                            valuesWorkingList.Clear();
                            valuesWorkingList = null;
                        }
                    }

                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        dict.Clear();
                        if (keysWorkingList == null)
                        {
                            Log.Error("Cannot fill dictionary because there are no keys.");
                        }
                        else if (valuesWorkingList == null)
                        {
                            Log.Error("Cannot fill dictionary because there are no values.");
                        }
                        else
                        {
                            if (keysWorkingList.Count != valuesWorkingList.Count)
                            {
                                Log.Error(string.Concat(new object[]
                                {
                                    "Keys count does not match the values count while loading a dictionary (maybe keys and values were resolved during different passes?). Some elements will be skipped. keys=",
                                    keysWorkingList.Count,
                                    ", values=",
                                    valuesWorkingList.Count
                                }));
                            }

                            int num = Math.Min(keysWorkingList.Count, valuesWorkingList.Count);
                            for (int i = 0; i < num; i++)
                            {
                                if (keysWorkingList[i] == null)
                                {
                                    Log.Error(string.Concat(new object[]
                                    {
                                        "Null key while loading dictionary of ",
                                        typeof(K),
                                        " and ",
                                        typeof(V),
                                        "."
                                    }));
                                }
                                else
                                {
                                    try
                                    {
                                        dict.Add(keysWorkingList[i], valuesWorkingList[i]);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(string.Concat(new object[]
                                        {
                                            "Exception in LookDictionary(node=",
                                            label,
                                            "): ",
                                            ex
                                        }));
                                    }
                                }
                            }
                        }
                    }

                    if (Scribe.mode == LoadSaveMode.PostLoadInit)
                    {
                        if (keysWorkingList != null)
                        {
                            keysWorkingList.Clear();
                            keysWorkingList = null;
                        }

                        if (valuesWorkingList != null)
                        {
                            valuesWorkingList.Clear();
                            valuesWorkingList = null;
                        }
                    }
                }
                finally
                {
                    Scribe.ExitNode();
                }
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                dict = null;
            }
        }

        public static void LookValue<T>(T t, string label, bool force = false)
        {
            Scribe_Values.Look(ref t, label, forceSave: force);
        }

        public static void LookDeep<T>(T t, string label)
        {
            Scribe_Deep.Look(ref t, label);
        }

        public static void LookULong(ref ulong value, string label, ulong defaultValue = 0)
        {
            string valueStr = value.ToString();
            Scribe_Values.Look(ref valueStr, label, defaultValue.ToString());
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                ulong.TryParse(valueStr, out value);
        }

        public static void LookRectDict<K>(ref Dictionary<K, Rect> dict, string label)
        {
            Dictionary<K, Vector4> backingLookup = new Dictionary<K, Vector4>();
            foreach (K key in dict.Keys)
            {
                Rect r = dict[key];
                backingLookup.Add(key, new Vector4(r.x, r.y, r.width, r.height));
            }

            ScribeUtil.LookWithValueKey<K, Vector4>(ref backingLookup, "windowRectLookup", LookMode.Value, LookMode.Value);

            dict = new Dictionary<K, Rect>();
            foreach (K key in backingLookup.Keys)
            {
                Vector4 v = backingLookup[key];
                dict.Add(key, new Rect(v.x, v.y, v.z, v.w));
            }
        }

        public static void LookRect(ref Rect rect, string label)
        {
            Vector4 value = new Vector4(rect.x, rect.y, rect.width, rect.height);
            Scribe_Values.Look(ref value, label);
            rect = new Rect(value.x, value.y, value.z, value.w);
        }
    }
}
