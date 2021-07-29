using System.Collections.Generic;
using System.Xml;

namespace Multiplayer.Client
{
    public class CustomXmlElement : XmlElement
    {
        public object val;

        public int nodes;

        public Dictionary<string, XmlNode> nodeDict;

        public bool usedOutside = true;

        public static long n;

        public static long m;

        public override XmlElement this[string name]
        {
            get
            {
                m++;
                if (nodeDict != null)
                {
                    n++;
                    nodeDict.TryGetValue(name, out XmlNode value);
                    return value as XmlElement;
                }
                for (XmlNode xmlNode = FirstChild; xmlNode != null; xmlNode = xmlNode.NextSibling)
                {
                    n++;
                    if (xmlNode.NodeType == XmlNodeType.Element && xmlNode.Name == name)
                    {
                        return (XmlElement)xmlNode;
                    }
                }
                return null;
            }
        }

        public CustomXmlElement(string prefix, string localName, string namespaceURI, XmlDocument doc): base(prefix, localName, namespaceURI, doc)
        {
        }

        public override XmlNode AppendChild(XmlNode newChild)
        {
            XmlNode result = base.AppendChild(newChild);
            if (!usedOutside)
            {
                nodes++;
                if (nodeDict != null)
                {
                    nodeDict[newChild.Name] = newChild;
                }
                if (nodes == 4)
                {
                    nodeDict = new Dictionary<string, XmlNode>();
                    for (XmlNode xmlNode = FirstChild; xmlNode != null; xmlNode = xmlNode.NextSibling)
                    {
                        nodeDict[xmlNode.Name] = xmlNode;
                    }
                }
            }
            return result;
        }

        public override XmlNode RemoveChild(XmlNode oldChild)
        {
            usedOutside = true;
            nodeDict = null;
            return base.RemoveChild(oldChild);
        }

        public override XmlNode InsertAfter(XmlNode newChild, XmlNode refChild)
        {
            usedOutside = true;
            nodeDict = null;
            return base.InsertAfter(newChild, refChild);
        }

        public override XmlNode InsertBefore(XmlNode newChild, XmlNode refChild)
        {
            usedOutside = true;
            nodeDict = null;
            return base.InsertBefore(newChild, refChild);
        }
    }
}
