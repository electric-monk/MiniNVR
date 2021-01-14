using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Xml;
using System.Xml.Serialization;

namespace TestConsole
{
    class OnvifEvents
    {
        public class NotificationServer
        {
            private readonly HttpListener listener;
            private readonly int port;

            public HttpListener Server
            {
                get
                {
                    return listener;
                }
            }

            public int Port
            {
                get
                {
                    return port;
                }
            }

            public NotificationServer()
            {
                Random rando = new Random();
                List<int> used = new List<int>();
                while (true)
                {
                    port = rando.Next(49152, 65535);
                    if (used.Contains(port))
                        continue;
                    listener = new HttpListener();
                    listener.Prefixes.Add(string.Format("http://+:{0}/", port));
                    try
                    {
                        listener.Start();
                    }
                    catch (Exception e)
                    {
                        System.Console.WriteLine(e);
                        used.Add(Port);
                        continue;
                    }
                    break;
                }
            }
        }

        private static string StripName(string name)
        {
            try
            {
                return name.Split(':')[1];
            }
            catch
            {
                return name;
            }
        }

        private static KeyValuePair<string, string> ParseSimpleItem(XmlElement simpleItem)
        {
            return new KeyValuePair<string, string>(simpleItem.GetAttribute("Name"), simpleItem.GetAttribute("Value"));
        }

        private static Dictionary<string, string> ParseData(XmlElement element)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (element != null)
            {
                foreach (XmlElement child in element.ChildNodes)
                {
                    KeyValuePair<string, string> kvp = ParseSimpleItem(child);
                    result.Add(kvp.Key, kvp.Value);
                }
            }
            return result;
        }

        public class OnvifEvent
        {
            public string Topic { get; }
            public DateTime Timestamp { get;  }
            public string Type { get; }

            public Dictionary<string, string> Source { get; }

            public Dictionary<string, string> Data { get; }

            public OnvifEvent(string topic, DateTime time, string type, XmlElement sourceElement, XmlElement dataElement)
            {
                Topic = topic;
                Timestamp = time;
                Type = type;
                Source = ParseData(sourceElement);
                Data = ParseData(dataElement);
            }
        }

        public static OnvifEvent[] ParseEvents(System.IO.Stream input)
        {
            XmlSerializer bodySerialiser = new XmlSerializer(typeof(Onvif.Event.Notify), new XmlRootAttribute
            {
                ElementName = "Notify",
                IsNullable = false,
                Namespace = "http://docs.oasis-open.org/wsn/b-2"
            });
            Onvif.Event.Notify body = (Onvif.Event.Notify)bodySerialiser.Deserialize(SoapHelper.StripEnvelope(input));
            List<OnvifEvent> results = new List<OnvifEvent>();
            foreach(Onvif.Event.NotificationMessageHolderType holder in body.NotificationMessage)
            {
                XmlElement source = null, data = null;
                foreach (XmlElement child in holder.Message.ChildNodes)
                {
                    string name = StripName(child.Name);
                    if (name.Equals("Source"))
                        source = child;
                    else if (name.Equals("Data"))
                        data = child;
                }
                results.Add(new OnvifEvent(StripName(holder.Topic.Any[0].Value), DateTime.Parse(holder.Message.GetAttribute("UtcTime")), holder.Message.GetAttribute("PropertyOperation"), source, data));
            }
            return results.ToArray();
        }

    }
}
