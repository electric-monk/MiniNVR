using System.Xml;
using System.Xml.Serialization;

namespace TestConsole
{
    public class SoapHelper
    {
        public class Internal
        {
            public class Header
            {

            }

            [XmlRoot(Namespace = v)]
            public class Envelope
            {
                private const string v = "http://www.w3.org/2003/05/soap-envelope";

                public Header Header { get; set; }

                public XmlElement Body { get; set; }

                static Envelope()
                {
                    staticxmlns = new XmlSerializerNamespaces();
                    staticxmlns.Add("env", v);
                }

                private static XmlSerializerNamespaces staticxmlns;

                [XmlNamespaceDeclarations]
                public XmlSerializerNamespaces xmlns { get { return staticxmlns; } set { } }
            }
        }

        public static XmlNodeReader StripEnvelope(System.IO.Stream stream)
        {
            XmlSerializer serialiser = new XmlSerializer(typeof(Internal.Envelope));
            Internal.Envelope data = (Internal.Envelope)serialiser.Deserialize(stream);
            return new XmlNodeReader(data.Body);
        }
    }
}
