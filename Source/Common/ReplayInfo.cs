using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

public class ReplayInfo
{
    public string name;
    public int protocol;
    public int playerFaction;
    public int spectatorFaction;

    public List<ReplaySection> sections = new();

    public string rwVersion;
    public List<string> modIds;
    public List<string> modNames;
    public List<int> modAssemblyHashes; // Unused, here to satisfy DirectXmlToObject on old saves

    public XmlBool asyncTime;
    public bool multifaction;
    public int markerCapPerPlayer = PingMarkerCap.Default;

    public static byte[] Write(ReplayInfo info)
    {
        var stream = new MemoryStream();
        var ns = new XmlSerializerNamespaces();
        ns.Add("", "");
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true
        };
        using var writer = XmlWriter.Create(stream, settings);
        GetSerializer().Serialize(writer, info, ns);
        return stream.ToArray();
    }

    public static ReplayInfo Read(byte[] xml)
    {
        var info = (ReplayInfo)GetSerializer().Deserialize(new MemoryStream(xml))!;
        // Defend against hand-edited or corrupt headers; saves themselves clamp on LoadingVars.
        info.markerCapPerPlayer = PingMarkerCap.Clamp(info.markerCapPerPlayer);
        return info;
    }

    private static XmlSerializer GetSerializer()
    {
        var overrides = new XmlAttributeOverrides();

        overrides.Add(typeof(ReplayInfo), nameof(sections), new XmlAttributes
        {
            XmlArrayItems = { new XmlArrayItemAttribute("li") }
        });
        overrides.Add(typeof(ReplayInfo), nameof(modIds), new XmlAttributes
        {
            XmlArrayItems = { new XmlArrayItemAttribute("li") }
        });
        overrides.Add(typeof(ReplayInfo), nameof(modNames), new XmlAttributes
        {
            XmlArrayItems = { new XmlArrayItemAttribute("li") }
        });

        return new XmlSerializer(typeof(ReplayInfo), overrides);
    }
}

public class ReplaySection
{
    public int start;
    public int end;

    // ReSharper disable once UnusedMember.Global
    public ReplaySection()
    {
    }

    public ReplaySection(int start, int end)
    {
        this.start = start;
        this.end = end;
    }
}

// Taken from StackOverflow, makes bool serialization case-insensitive
public struct XmlBool : IXmlSerializable
{
    private bool value;

    public static implicit operator bool(XmlBool yn)
    {
        return yn.value;
    }

    public static implicit operator XmlBool(bool b)
    {
        return new XmlBool { value = b };
    }

    public XmlSchema? GetSchema()
    {
        return null;
    }

    public void ReadXml(XmlReader reader)
    {
        var s = reader.ReadElementContentAsString().ToLowerInvariant();
        value = s is "true" or "yes" or "y";
    }

    public void WriteXml(XmlWriter writer)
    {
        writer.WriteString(value ? "true" : "false");
    }
}
