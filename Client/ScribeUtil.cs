using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Verse;

namespace Multiplayer.Client
{
    public class SharedCrossRefs : LoadedObjectDirectory
    {
        // Used in CrossRefs patches
        public HashSet<string> tempKeys = new HashSet<string>();

        public Dictionary<string, ILoadReferenceable> Dict { get => allObjectsByLoadID; }

        public void Unregister(ILoadReferenceable thing)
        {
            Unregister(thing.GetUniqueLoadID());
        }

        public void Unregister(string key)
        {
            Dict.Remove(key);
        }

        public void UnregisterAllFrom(Map map)
        {
            Dict.RemoveAll(x => x.Value is Thing t && t.Map == map);
        }
    }

    public static class ScribeUtil
    {
        private const string ROOT_NODE = "root";

        private static MemoryStream stream;

        public static SharedCrossRefs sharedCrossRefs => Multiplayer.game.sharedCrossRefs;
        public static LoadedObjectDirectory defaultCrossRefs;

        public static bool loading;

        public static Type XmlNodeWriter = AccessTools.TypeByName("System.Xml.XmlNodeWriter");
        public static PropertyInfo GetDocumentProperty = AccessTools.Property(XmlNodeWriter, "Document");

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
            XmlWriter writer = (XmlWriter)Activator.CreateInstance(XmlNodeWriter);
            Scribe.saver.writer = writer;
            writer.WriteStartDocument();
        }

        public static XmlDocument FinishWritingToDoc()
        {
            XmlWriter writer = Scribe.saver.writer;
            XmlDocument doc = (XmlDocument)GetDocumentProperty.GetValue(writer, null);
            Scribe.saver.writer = null;
            Scribe.saver.ExitNode();
            writer.WriteEndDocument();
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
            StartLoading(GetDocument(data));
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

        public static XmlDocument GetDocument(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data))
            using (XmlReader reader = XmlReader.Create(stream))
            {
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.Load(reader);
                return xmlDocument;
            }
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

            Log.Message("Cross ref supply: " + sharedCrossRefs.Dict.Count + " " + sharedCrossRefs.Dict.Last() + " " + Faction.OfPlayer);
        }

        public static byte[] WriteExposable(IExposable element, string name = ROOT_NODE, bool indent = false)
        {
            StartWriting(indent);
            Scribe.EnterNode(ROOT_NODE);
            Scribe_Deep.Look(ref element, name);
            return FinishWriting();
        }

        public static T ReadExposable<T>(byte[] data, Action<T> beforeFinish = null) where T : IExposable
        {
            StartLoading(data);
            SupplyCrossRefs();
            T element = default(T);
            Scribe_Deep.Look(ref element, ROOT_NODE);

            beforeFinish?.Invoke(element);

            FinalizeLoading();
            return element;
        }

        /// <summary>
        /// Dictionary Look with value type keys
        /// </summary>
        public static void Look<K, V>(ref Dictionary<K, V> dict, string label, LookMode valueLookMode, params object[] valueCtorArgs)
        {
            List<V> list = null;
            Look(ref dict, label, valueLookMode, ref list, valueCtorArgs);
        }

        /// <summary>
        /// Dictionary Look with value type keys
        /// </summary>
        public static void Look<K, V>(ref Dictionary<K, V> dict, string label, LookMode valueLookMode, ref List<V> valuesWorkingList, params object[] valueCtorArgs)
        {
            LookMode keyLookMode = LookMode.Value;
            List<K> keysWorkingList = null;

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

                    Scribe_Collections.Look(ref keysWorkingList, "keys", keyLookMode);
                    Scribe_Collections.Look(ref valuesWorkingList, "values", valueLookMode, valueCtorArgs);

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

                    bool flag = keyLookMode == LookMode.Reference || valueLookMode == LookMode.Reference;
                    if ((flag && Scribe.mode == LoadSaveMode.ResolvingCrossRefs) || (!flag && Scribe.mode == LoadSaveMode.LoadingVars))
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
    }
}
