using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Multiplayer.Client.Util;
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
            thingsById.RemoveAll(kv => kv.Value.Map == map);
        }
    }

    public static class ScribeUtil
    {
        private const string RootNode = "root";

        private static MemoryStream writingStream;

        public static SharedCrossRefs sharedCrossRefs => Multiplayer.game.sharedCrossRefs;
        public static LoadedObjectDirectory defaultCrossRefs;

        public static bool loading;

        public static void StartWriting(bool indent = false)
        {
            writingStream = new MemoryStream();

            Scribe.mode = LoadSaveMode.Saving;
            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings()
            {
                Indent = indent,
                OmitXmlDeclaration = true
            };

            XmlWriter writer = XmlWriter.Create(writingStream, xmlWriterSettings);
            Scribe.saver.writer = writer;
            writer.WriteStartDocument();
        }

        public static byte[] FinishWriting()
        {
            Scribe.saver.FinalizeSaving();

            byte[] arr = writingStream.ToArray();
            writingStream.Close();
            writingStream = null;

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

            using MemoryStream stream = new MemoryStream();
            using XmlWriter writer = XmlWriter.Create(stream, xmlWriterSettings);

            if (rootNode != null)
                writer.WriteStartElement(rootNode);

            node.WriteTo(writer);

            if (rootNode != null)
                writer.WriteEndElement();

            writer.Flush();
            return stream.ToArray();
        }

        public static void SupplyCrossRefs()
        {
            if (sharedCrossRefs == null) return;

            if (!loading)
            {
                Log.Warning("Tried to supply cross refs without calling ScribeUtil.StartLoading()");
                return;
            }

            defaultCrossRefs ??= Scribe.loader.crossRefs.loadedObjectDirectory;
            Scribe.loader.crossRefs.loadedObjectDirectory = sharedCrossRefs;

            MpLog.Debug($"Cross ref supply: {sharedCrossRefs.allObjectsByLoadID.Count} {sharedCrossRefs.allObjectsByLoadID.LastOrDefault()} {Faction.OfPlayer}");
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
            T element = default;
            Scribe_Deep.Look(ref element, RootNode);

            beforeFinish?.Invoke(element);

            FinalizeLoading();

            // Default cross refs restored in LoadedObjectsClearPatch

            return element;
        }
    }
}
