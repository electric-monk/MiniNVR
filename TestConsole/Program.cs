using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel.Channels;
using System.ServiceModel;
using System.Net;
using System.Xml.Serialization;
using System.Xml;

namespace TestConsole
{
    public class Program
    {


        static void Main(string[] args)
        {
            BindingElement transportBindingElement = new HttpTransportBindingElement();
            WSHttpBinding transportBinding = new WSHttpBinding(SecurityMode.None);
            //transportBinding.Security.Transport.ClientCredentialType = HttpClientCredentialType.Digest;
            //transportBindingElement.AuthenticationScheme = System.Net.AuthenticationSchemes.Digest;
            TextMessageEncodingBindingElement encodingBinding = new TextMessageEncodingBindingElement();
            encodingBinding.MessageVersion = MessageVersion.CreateVersion(EnvelopeVersion.Soap12, AddressingVersion.None);
            //CustomBinding bind = new CustomBinding(encodingBinding, transportBindingElement);
            Binding bind = transportBinding;
            EndpointAddress mediaAddress = new EndpointAddress("http://10.116.205.233/onvif/device_service");
            Onvif.Device.DeviceClient deviceClient = new Onvif.Device.DeviceClient(bind, mediaAddress);
            deviceClient.Endpoint.EndpointBehaviors.Add(new UsernameTokenEndpointBehaviour("admin", "Password1"));
            Onvif.Device.Capabilities dcaps = deviceClient.GetCapabilities(new Onvif.Device.CapabilityCategory[] { Onvif.Device.CapabilityCategory.All });
            Console.Out.WriteLine(dcaps.Device.ToString());
            Onvif.Media.MediaClient client = new Onvif.Media.MediaClient(new WSHttpBinding(SecurityMode.None), new EndpointAddress(dcaps.Media.XAddr));
            client.Endpoint.EndpointBehaviors.Add(new UsernameTokenEndpointBehaviour("admin", "Password1"));
            //client.ClientCredentials.UserName.UserName = "admin";
            //client.ClientCredentials.UserName.Password = "Password1";
            Onvif.Media.Capabilities caps = client.GetServiceCapabilities();
            Console.Out.WriteLine(caps);
            Onvif.Media.Profile[] profiles = client.GetProfiles();
            Onvif.Media.MediaUri uri = client.GetSnapshotUri(profiles[0].token);
            Console.Out.WriteLine("Snapshot URI: {0}", uri.Uri);
            uri = client.GetStreamUri(new Onvif.Media.StreamSetup(), profiles[0].token);
            Console.Out.WriteLine("Stream URI: {0}", uri.Uri);

            string localHost = "10.116.205.15";
            OnvifEvents.NotificationServer listener = new OnvifEvents.NotificationServer();
            string notifyUrl = string.Format("http://{0}:{1}/guid", localHost, listener.Port);
            System.Console.WriteLine(listener.Port);

            Onvif.Event.NotificationProducerClient nClient = new Onvif.Event.NotificationProducerClient(new WSHttpBinding(SecurityMode.None), new EndpointAddress(dcaps.Events.XAddr));
            nClient.Endpoint.EndpointBehaviors.Add(new UsernameTokenEndpointBehaviour("admin", "Password1"));
            Onvif.Event.Subscribe subscribey = new Onvif.Event.Subscribe()
            {
                ConsumerReference = new Onvif.Event.EndpointReferenceType()
                {
                    Address = new Onvif.Event.AttributedURIType()
                    {
                        Value = notifyUrl
                    }
                },
                InitialTerminationTime = "P10M"
            };
            nClient.Subscribe(subscribey);

            while (true)
            {
                HttpListenerContext context = listener.Server.GetContext();
                System.Console.WriteLine("Got request");
                // Handle data
                HttpListenerRequest request = context.Request;
                System.Console.WriteLine(request.RawUrl.Substring(1));

                var data = OnvifEvents.ParseEvents(request.InputStream);
                foreach(OnvifEvents.OnvifEvent ev in data) {
                    System.Console.Write("At {0}: {1} {2} [", ev.Timestamp, ev.Topic, ev.Type);
                    foreach (var src in ev.Source) {
                        System.Console.Write("{0}: {1}; ", src.Key, src.Value);
                    }
                    System.Console.Write("] ");
                    foreach (var src in ev.Data)
                    {
                        System.Console.Write("{0}: {1}; ", src.Key, src.Value);
                    }
                    System.Console.WriteLine();
                }

                // Send response
                HttpListenerResponse response = context.Response;
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes("Done");
                response.ContentLength64 = buffer.Length;
                System.IO.Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                output.Close();
            }
        }
    }
}
