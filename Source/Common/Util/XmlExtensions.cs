using System.Xml;

namespace Multiplayer.Common;

public static class XmlExtensions
{
    public static void SelectAndRemove(this XmlNode node, string xpath)
    {
        XmlNodeList nodes = node.SelectNodes(xpath);
        foreach (XmlNode selected in nodes)
            selected.RemoveFromParent();
    }

    public static void RemoveChildIfPresent(this XmlNode node, string child)
    {
        XmlNode childNode = node[child];
        if (childNode != null)
            node.RemoveChild(childNode);
    }

    public static void RemoveFromParent(this XmlNode node)
    {
        if (node == null) return;
        node.ParentNode.RemoveChild(node);
    }

    public static void AddNode(this XmlNode parent, string name, string value)
    {
        XmlNode node = parent.OwnerDocument.CreateElement(name);
        node.InnerText = value;
        parent.AppendChild(node);
    }
}
