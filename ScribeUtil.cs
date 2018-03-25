using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using Verse;

namespace Multiplayer
{
    public class CrossRefSupply : LoadedObjectDirectory
    {
        private static readonly FieldInfo dictField = typeof(LoadedObjectDirectory).GetField("allObjectsByLoadID", BindingFlags.NonPublic | BindingFlags.Instance);

        public List<string> tempKeys = new List<string>();

        public void Unregister(ILoadReferenceable thing)
        {
            Unregister(thing.GetUniqueLoadID());
        }

        public void Unregister(string key)
        {
            GetDict().Remove(key);
        }

        public void UnregisterAllFrom(Map map)
        {
            int a = GetDict().RemoveAll(x => x.Value is Thing && ((Thing)x.Value).Map == map);
        }

        public Dictionary<string, ILoadReferenceable> GetDict()
        {
            return (Dictionary<string, ILoadReferenceable>)dictField.GetValue(this);
        }
    }

    public class AttributedExposable : IExposable
    {
        private static readonly MethodInfo lookValue = typeof(Scribe_Values).GetMethod("Look");
        private static readonly MethodInfo lookDeep = typeof(Scribe_Deep).GetMethods().First(m => m.Name == "Look" && m.GetParameters().Length == 3);
        private static readonly MethodInfo lookReference = typeof(Scribe_References).GetMethod("Look");

        public virtual void ExposeData()
        {
            foreach (FieldInfo field in GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.TryGetAttribute(out ExposeValueAttribute exposeValue))
                {
                    object[] args = new object[] { field.GetValue(this), field.Name, null, false };
                    lookValue.MakeGenericMethod(field.FieldType).Invoke(null, args);
                    field.SetValue(this, args[0]);
                }
                else if (field.TryGetAttribute(out ExposeDeepAttribute exposeDeep))
                {
                    object[] args = new object[] { field.GetValue(this), field.Name, new object[0] };
                    lookDeep.MakeGenericMethod(field.FieldType).Invoke(null, args);
                    field.SetValue(this, args[0]);
                }
                else if (field.TryGetAttribute(out ExposeReferenceAttribute exposeReference))
                {
                    object[] args = new object[] { field.GetValue(this), field.Name, false };
                    lookReference.MakeGenericMethod(field.FieldType).Invoke(null, args);
                    field.SetValue(this, args[0]);
                }
            }
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ExposeValueAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ExposeDeepAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ExposeReferenceAttribute : Attribute
    {
    }

    public static class ScribeUtil
    {
        private static MemoryStream stream;
        private static readonly FieldInfo writerField = typeof(ScribeSaver).GetField("writer", BindingFlags.NonPublic | BindingFlags.Instance);

        public static CrossRefSupply crossRefs;
        public static LoadedObjectDirectory defaultCrossRefs;
        public static readonly FieldInfo crossRefsField = typeof(CrossRefHandler).GetField("loadedObjectDirectory", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool loading;

        public static void StartWriting(bool indent = false)
        {
            stream = new MemoryStream();

            Scribe.mode = LoadSaveMode.Saving;
            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings();
            xmlWriterSettings.Indent = indent;
            xmlWriterSettings.OmitXmlDeclaration = true;
            XmlWriter writer = XmlWriter.Create(stream, xmlWriterSettings);
            writerField.SetValue(Scribe.saver, writer);
            writer.WriteStartDocument();
        }

        public static byte[] FinishWriting()
        {
            Scribe.saver.FinalizeSaving();
            byte[] arr = stream.ToArray();
            stream = null;
            return arr;
        }

        public static void StartLoading(byte[] data)
        {
            loading = true;

            ScribeMetaHeaderUtility.loadedGameVersion = VersionControl.CurrentVersionStringWithRev;

            using (MemoryStream stream = new MemoryStream(data))
            using (XmlReader xml = XmlReader.Create(stream))
            {
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.Load(xml);
                Scribe.loader.curXmlParent = xmlDocument.DocumentElement;
            }

            Scribe.mode = LoadSaveMode.LoadingVars;
        }

        [HarmonyPatch(typeof(ScribeLoader))]
        [HarmonyPatch(nameof(ScribeLoader.FinalizeLoading))]
        public static class FinalizeLoadingPatch
        {
            static void Postfix() => loading = false;
        }

        public static void FinishLoading()
        {
            Scribe.loader.FinalizeLoading();
        }

        public static void SupplyCrossRefs()
        {
            if (crossRefs == null) return;

            if (!loading)
            {
                Log.Warning("Tried to supply cross refs without calling ScribeUtil.StartLoading()");
                return;
            }

            if (defaultCrossRefs == null)
                defaultCrossRefs = (LoadedObjectDirectory)crossRefsField.GetValue(Scribe.loader.crossRefs);

            crossRefsField.SetValue(Scribe.loader.crossRefs, crossRefs);

            Log.Message("Cross ref supply: " + crossRefs.GetDict().Count + " " + crossRefs.GetDict().Last() + " " + Faction.OfPlayer);
        }

        public static byte[] WriteSingle(IExposable element, string name = "data", bool indent = false)
        {
            StartWriting(indent);
            Scribe.EnterNode("data");
            Scribe_Deep.Look(ref element, name);
            return FinishWriting();
        }

        public static T ReadSingle<T>(byte[] data, Action<T> beforeFinish = null) where T : IExposable
        {
            StartLoading(data);
            SupplyCrossRefs();
            T element = default(T);
            Scribe_Deep.Look(ref element, "data");

            if (beforeFinish != null)
                beforeFinish.Invoke(element);

            FinishLoading();
            return element;
        }

        public static void Look<K, V>(ref Dictionary<K, V> dict, string label, LookMode keyLookMode, LookMode valueLookMode)
        {
            List<K> list1 = null;
            List<V> list2 = null;
            Look(ref dict, label, keyLookMode, valueLookMode, ref list1, ref list2);
        }

        public static void Look<K, V>(ref Dictionary<K, V> dict, string label, LookMode keyLookMode, LookMode valueLookMode, ref List<K> keysWorkingList, ref List<V> valuesWorkingList)
        {
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

                    Scribe_Collections.Look(ref keysWorkingList, "keys", keyLookMode, new object[0]);
                    Scribe_Collections.Look(ref valuesWorkingList, "values", valueLookMode, new object[0]);

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
