using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

        private class HelpyFrame
        {
            public event EventHandler<RtspClientSharp.RawFrames.Video.RawH264Frame> OnFrame;

            private static ArraySegment<byte> SaveBuffer(ArraySegment<byte> buffer)
            {
                return new ArraySegment<byte>(buffer.ToArray());
            }

            public static RtspClientSharp.RawFrames.Video.RawH264Frame CopyFrame(RtspClientSharp.RawFrames.Video.RawH264Frame frame)
            {
                RtspClientSharp.RawFrames.Video.RawH264Frame duplicate = null;
                if (frame is RtspClientSharp.RawFrames.Video.RawH264IFrame iFrame) {
                    duplicate = new RtspClientSharp.RawFrames.Video.RawH264IFrame(iFrame.Timestamp, SaveBuffer(iFrame.FrameSegment), SaveBuffer(iFrame.SpsPpsSegment));
                } else if (frame is RtspClientSharp.RawFrames.Video.RawH264PFrame pFrame) {
                    duplicate = new RtspClientSharp.RawFrames.Video.RawH264PFrame(pFrame.Timestamp, SaveBuffer(pFrame.FrameSegment));
                }
                return duplicate;
            }

            public void Send(RtspClientSharp.RawFrames.RawFrame frame)
            {
                if (frame is RtspClientSharp.RawFrames.Video.RawH264Frame framey)
                    OnFrame?.Invoke(this, CopyFrame(framey));
            }
        }

        private static byte[] AppendDataWithStartMarker(byte[][] data)
        {
            int total = 0;
            int i = 0;
            bool[] prepend = new bool[data.Length];
            foreach (var part in data) {
                prepend[i] = MP4.SpsParser.FindPattern(part, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, 0) != 0;
                if (prepend[i])
                    total += RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length;
                total += part.Length;
                i++;
            }
            if ((prepend.Length == 1) && !prepend[0])
                return data[0];
            byte[] result = new byte[total];
            int j = 0, k = 0;
            foreach (var part in data) {
                if (prepend[k]) {
                    Buffer.BlockCopy(RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, 0, result, j, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length);
                    j += RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length;
                }
                Buffer.BlockCopy(part, 0, result, j, part.Length);
                j += part.Length;
                k++;
            }
            return result;
        }

        private class LiveVideoEndpoint : WebServer.IEndpoint
        {
            private readonly HelpyFrame frameSource;

            public LiveVideoEndpoint(HelpyFrame frameHelper)
            {
                frameSource = frameHelper;
            }

            private class Streamer
            {
                private readonly new BlockingCollection<RtspClientSharp.RawFrames.Video.RawH264Frame> queue;
                private readonly Thread queueThread;
                private readonly HelpyFrame frameSource;
                private readonly System.IO.Stream stream;
                private readonly List<byte[]> samples;
                private MP4.SpsParser parser;
                private readonly MP4.Mp4Helper mp4File;

                public Streamer(HelpyFrame frameHelper, HttpListenerResponse response)
                {
                    queue = new BlockingCollection<RtspClientSharp.RawFrames.Video.RawH264Frame>(new ConcurrentQueue<RtspClientSharp.RawFrames.Video.RawH264Frame>());
                    queueThread = new Thread(WorkerThread);
                    queueThread.Name = "MP4 Streamer";
                    frameSource = frameHelper;
                    response.ContentType = "video/mp4";
                    stream = response.OutputStream;
                    samples = new List<byte[]>();
                    mp4File = new MP4.Mp4Helper();
                    frameSource.OnFrame += FrameHandle;
                    queueThread.Start();
                }

                private void FrameHandle(object sender, RtspClientSharp.RawFrames.Video.RawH264Frame frame)
                {
                    queue.Add(frame);
                }
                private void WorkerThread()
                {
                    while (true) {
                        if (!HandleFrame(queue.Take()))
                            break;
                    }
                }

                private bool HandleFrame(RtspClientSharp.RawFrames.Video.RawH264Frame frame)
                {
                    try {
                        bool isIFrame = false;
                        if (frame is RtspClientSharp.RawFrames.Video.RawH264IFrame iframe) {
                            isIFrame = true;
                            if (parser == null) {
                                parser = new MP4.SpsParser(iframe.SpsPpsSegment.ToArray());
                                mp4File.CreateEmptyMP4(parser).SaveTo(stream);
                            } else {
                                var boxes = mp4File.CreateChunk(samples.ToArray());
                                samples.Clear();
                                foreach (var box in boxes)
                                    box.ToStream(stream);
                            }
                        }
                        if (parser != null) {
                            byte[] sample = frame.FrameSegment.ToArray();
                            byte[][] parts;
                            if (isIFrame) {
                                // Apparently, I frame should also be preceded by SPS and PPS, even though they're also in the header.
                                parts = new byte[][] { parser.Sps, parser.Pps, sample };
                            } else {
                                // Check it has a start marker (as we use it to update it to a length marker)
                                parts = new byte[][] { sample };
                            }
                            samples.Add(AppendDataWithStartMarker(parts));
                        }
                        return true;
                    }
                    catch (HttpListenerException) {
                        frameSource.OnFrame -= FrameHandle;
                        stream.Close();
                        return false;
                    }
                }
            }

            public void Handle(HttpListenerContext request)
            {
                new Streamer(frameSource, request.Response);
            }
        }

        private static async Task StreamAsync(HelpyFrame frameHelper, string rtspUri, string username, string password, CancellationToken token)
        {
            try
            {
                TimeSpan delay = TimeSpan.FromSeconds(5);
                var parameters = new RtspClientSharp.ConnectionParameters(new Uri(rtspUri), new NetworkCredential(username, password));
                using (var rtspClient = new RtspClientSharp.RtspClient(parameters))
                {
                    rtspClient.FrameReceived += (sender, frame) => frameHelper.Send(frame);
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
            HelpyFrame frames = new HelpyFrame();
            WebInterface web = new WebInterface();
            web.Server.AddContent("/stream", new LiveVideoEndpoint(frames));

            WSHttpBinding transportBinding = new WSHttpBinding(SecurityMode.None);
            TextMessageEncodingBindingElement encodingBinding = new TextMessageEncodingBindingElement();
            encodingBinding.MessageVersion = MessageVersion.CreateVersion(EnvelopeVersion.Soap12, AddressingVersion.None);
            Binding bind = transportBinding;
            EndpointAddress mediaAddress = new EndpointAddress("http://10.116.205.233/onvif/device_service");
            Onvif.Device.DeviceClient deviceClient = new Onvif.Device.DeviceClient(bind, mediaAddress);
            deviceClient.Endpoint.EndpointBehaviors.Add(new Onvif.Soap.UsernameTokenEndpointBehaviour("admin", "Password1"));
            Onvif.Device.Capabilities dcaps = deviceClient.GetCapabilities(new Onvif.Device.CapabilityCategory[] { Onvif.Device.CapabilityCategory.All });
            Console.Out.WriteLine(dcaps.Device.ToString());
            Onvif.Media.MediaClient client = new Onvif.Media.MediaClient(new WSHttpBinding(SecurityMode.None), new EndpointAddress(dcaps.Media.XAddr));
            client.Endpoint.EndpointBehaviors.Add(new Onvif.Soap.UsernameTokenEndpointBehaviour("admin", "Password1"));
            Onvif.Media.Capabilities caps = client.GetServiceCapabilities();
            Console.Out.WriteLine(caps);
            Onvif.Media.Profile[] profiles = client.GetProfiles();
            Onvif.Media.MediaUri uri = client.GetSnapshotUri(profiles[0].token);
            Console.Out.WriteLine("Snapshot URI: {0}", uri.Uri);
            uri = client.GetStreamUri(new Onvif.Media.StreamSetup(), profiles[0].token);
            Console.Out.WriteLine("Stream URI: {0}", uri.Uri);

            Task streamTask = StreamAsync(frames, uri.Uri, "admin", "Password1", new CancellationTokenSource().Token);

            string localHost = "10.116.205.15";
            Onvif.Events.NotificationServer listener = new Onvif.Events.NotificationServer();
            string notifyUrl = string.Format("http://{0}:{1}/guid", localHost, listener.Port);
            System.Console.WriteLine(listener.Port);

            Onvif.Event.NotificationProducerClient nClient = new Onvif.Event.NotificationProducerClient(new WSHttpBinding(SecurityMode.None), new EndpointAddress(dcaps.Events.XAddr));
            nClient.Endpoint.EndpointBehaviors.Add(new Onvif.Soap.UsernameTokenEndpointBehaviour("admin", "Password1"));
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

                var data = Onvif.Events.ParseEvents(request.InputStream);
                foreach(Onvif.Events.Event ev in data) {
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
