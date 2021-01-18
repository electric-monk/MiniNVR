using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
        private static async Task StreamAsync(string rtspUri, string username, string password, CancellationToken token)
        {
            try
            {
                TimeSpan delay = TimeSpan.FromSeconds(5);
                var parameters = new RtspClientSharp.ConnectionParameters(new Uri(rtspUri), new NetworkCredential(username, password));
                using (var rtspClient = new RtspClientSharp.RtspClient(parameters))
                {
                    Mp4Helper helper = new Mp4Helper();
                    MatrixIO.IO.Bmff.BaseMedia container = null;
                    List<byte[]> samples = null;
                    int count = 0;
                    SpsParser parser = null;

                    rtspClient.FrameReceived += (sender, frame) => {
                        string extra = "";
                        if (frame is RtspClientSharp.RawFrames.Video.RawH264IFrame) {
                            RtspClientSharp.RawFrames.Video.RawH264IFrame iFrame = (RtspClientSharp.RawFrames.Video.RawH264IFrame)frame;
                            parser = new SpsParser(iFrame.SpsPpsSegment.Array);
                            extra = $" [dimensions {parser.Dimensions.Width}x{parser.Dimensions.Height}]";
                            if (container == null) {
                                container = helper.CreateEmptyMP4(parser);
                                samples = new List<byte[]>();
                            } else {
                                foreach (var item in helper.CreateChunk(samples.ToArray()))
                                    container.Children.Add(item);
                                samples.Clear();
                                count++;
                                if (count > 10) {
                                    count = 0;
                                    using (var f = System.IO.File.Create("frankentaunt.mp4")) {
                                        container.SaveTo(f);
                                    }
                                }
                            }
                        }
                        if (samples != null) {
                            byte[] sample = frame.FrameSegment.ToArray();
                            if (frame is RtspClientSharp.RawFrames.Video.RawH264IFrame) {
                                // Apparently, I frame should also be preceded by SPS and PPS, even though they're also in the header.
                                byte[] result = new byte[(RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length * 2) + parser.Sps.Length + parser.Pps.Length + sample.Length];
                                int i = 0;
                                Buffer.BlockCopy(RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, 0, result, i, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length);
                                i += RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length;
                                Buffer.BlockCopy(parser.Sps, 0, result, i, parser.Sps.Length);
                                i += parser.Sps.Length;
                                Buffer.BlockCopy(RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, 0, result, i, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length);
                                i += RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length;
                                Buffer.BlockCopy(parser.Pps, 0, result, i, parser.Pps.Length);
                                i += parser.Pps.Length;
                                Buffer.BlockCopy(sample, 0, result, i, sample.Length);
                                sample = result;
                            }
                            if (SpsParser.FindPattern(sample, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, 0) == -1) {
                                byte[] result = new byte[RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length + sample.Length];
                                Buffer.BlockCopy(RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, 0, result, 0, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length);
                                Buffer.BlockCopy(sample, 0, result, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length, sample.Length);
                                sample = result;
                            }
                            samples.Add(sample);
                            // TODO: Do something sensible with timestamp
                        }
                        Console.WriteLine($"New frame {frame.Timestamp}: {frame.GetType().Name} {extra}");
                    };
                    while (true)
                    {
                        Console.WriteLine("Connecting...");
                        try
                        {
                            await rtspClient.ConnectAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (RtspClientSharp.Rtsp.RtspClientException e)
                        {
                            Console.WriteLine(e.ToString());
                            await Task.Delay(delay, token);
                            continue;
                        }
                        Console.WriteLine("Connected");
                        try
                        {
                            await rtspClient.ReceiveAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (RtspClientSharp.Rtsp.RtspClientException e)
                        {
                            Console.WriteLine(e.ToString());
                            await Task.Delay(delay, token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Console.WriteLine("Operation cancelled?");
            }
        }

        static void Main(string[] args)
        {
            WebInterface web = new WebInterface();

            WSHttpBinding transportBinding = new WSHttpBinding(SecurityMode.None);
            TextMessageEncodingBindingElement encodingBinding = new TextMessageEncodingBindingElement();
            encodingBinding.MessageVersion = MessageVersion.CreateVersion(EnvelopeVersion.Soap12, AddressingVersion.None);
            Binding bind = transportBinding;
            EndpointAddress mediaAddress = new EndpointAddress("http://10.116.205.233/onvif/device_service");
            Onvif.Device.DeviceClient deviceClient = new Onvif.Device.DeviceClient(bind, mediaAddress);
            deviceClient.Endpoint.EndpointBehaviors.Add(new UsernameTokenEndpointBehaviour("admin", "Password1"));
            Onvif.Device.Capabilities dcaps = deviceClient.GetCapabilities(new Onvif.Device.CapabilityCategory[] { Onvif.Device.CapabilityCategory.All });
            Console.Out.WriteLine(dcaps.Device.ToString());
            Onvif.Media.MediaClient client = new Onvif.Media.MediaClient(new WSHttpBinding(SecurityMode.None), new EndpointAddress(dcaps.Media.XAddr));
            client.Endpoint.EndpointBehaviors.Add(new UsernameTokenEndpointBehaviour("admin", "Password1"));
            Onvif.Media.Capabilities caps = client.GetServiceCapabilities();
            Console.Out.WriteLine(caps);
            Onvif.Media.Profile[] profiles = client.GetProfiles();
            Onvif.Media.MediaUri uri = client.GetSnapshotUri(profiles[0].token);
            Console.Out.WriteLine("Snapshot URI: {0}", uri.Uri);
            uri = client.GetStreamUri(new Onvif.Media.StreamSetup(), profiles[0].token);
            Console.Out.WriteLine("Stream URI: {0}", uri.Uri);

            Task streamTask = StreamAsync(uri.Uri, "admin", "Password1", new CancellationTokenSource().Token);

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
            try {
                nClient.Subscribe(subscribey);
            }
            catch (FaultException e) {
                System.Console.WriteLine("Failed to subscribe: " + e.ToString());
            }

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
