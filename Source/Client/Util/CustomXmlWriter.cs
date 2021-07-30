using System;
using System.Xml;

namespace Multiplayer.Client
{
    public class CustomXmlWriter : XmlWriter
    {
        public XmlDocument doc;
        public XmlNode current;
        public XmlAttribute attr;

        public override WriteState WriteState => throw new NotImplementedException();

        public CustomXmlWriter()
        {
            doc = new XmlDocument();
            current = doc;
        }

        public override void Flush()
        {
        }

        public override void WriteStartDocument()
        {
        }

        public override void WriteEndDocument()
        {
        }

        public override void WriteStartElement(string prefix, string localName, string ns)
        {
            CustomXmlElement newChild = new CustomXmlElement(prefix, localName, ns, doc);
            current.AppendChild(newChild);
            current = newChild;
        }

        public override void WriteEndElement()
        {
            if (current.ParentNode is CustomXmlElement parent) {
                current = parent;
            } else {
                doc.AppendChild(current);
            }
        }

        public override void WriteString(string str)
        {
            XmlText newChild = doc.CreateTextNode(str);
            if (attr != null) {
                attr.AppendChild(newChild);
            } else {
                current.AppendChild(newChild);
            }
        }

        public override void WriteStartAttribute(string prefix, string localName, string ns)
        {
            attr = doc.CreateAttribute(prefix, localName, ns);
            ((XmlElement)current).SetAttributeNode(attr);
        }

        public override void WriteEndAttribute()
        {
            attr = null;
        }

        public override string LookupPrefix(string ns)
        {
            throw new NotImplementedException();
        }

        public override void WriteBase64(byte[] buffer, int index, int count)
        {
            throw new NotImplementedException();
        }

        public override void WriteCData(string text)
        {
            throw new NotImplementedException();
        }

        public override void WriteCharEntity(char ch)
        {
            throw new NotImplementedException();
        }

        public override void WriteChars(char[] buffer, int index, int count)
        {
            throw new NotImplementedException();
        }

        public override void WriteComment(string text)
        {
            throw new NotImplementedException();
        }

        public override void WriteDocType(string name, string pubid, string sysid, string subset)
        {
            throw new NotImplementedException();
        }

        public override void WriteEntityRef(string name)
        {
            throw new NotImplementedException();
        }

        public override void WriteFullEndElement()
        {
            throw new NotImplementedException();
        }

        public override void WriteProcessingInstruction(string name, string text)
        {
            throw new NotImplementedException();
        }

        public override void WriteRaw(char[] buffer, int index, int count)
        {
            throw new NotImplementedException();
        }

        public override void WriteRaw(string data)
        {
            throw new NotImplementedException();
        }

        public override void WriteStartDocument(bool standalone)
        {
            throw new NotImplementedException();
        }

        public override void WriteSurrogateCharEntity(char lowChar, char highChar)
        {
            throw new NotImplementedException();
        }

        public override void WriteWhitespace(string ws)
        {
            throw new NotImplementedException();
        }
    }
}
