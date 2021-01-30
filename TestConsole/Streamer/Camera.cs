using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace TestConsole.Streamer
{
    public class Camera
    {
        private readonly Configuration.Cameras.Camera settings;
        private readonly Thread startupThread;
        private readonly ManualResetEventSlim stop;
        private readonly Utils.StreamWatcher streamWatcher;
        private Onvif.DeviceLink deviceLink;
        private Onvif.MediaLink mediaLink;
        private Uri snapshotUri;
        private Uri streamUri;
        private int viewers;

        private Task streamTask;
        private CancellationTokenSource cancellationTokenSource;

        public string Identifier { get { return settings.Identifier; } }

        internal Camera(Configuration.Cameras.Camera camera)
        {
            cancellationTokenSource = new CancellationTokenSource();
            settings = camera;
            viewers = 0;
            streamWatcher = new Utils.StreamWatcher();
            startupThread = new Thread(WorkThread);
            stop = new ManualResetEventSlim(false);
            startupThread.Start();
        }

        internal void Stop()
        {
            stop.Set();
            startupThread.Join();
            if (deviceLink != null) {
                deviceLink.Stop();
                deviceLink = null;
            }
            if (mediaLink != null) {
                mediaLink.Stop();
                mediaLink = null;
            }
            snapshotUri = null;
            streamUri = null;
            StopStream(true);
            streamWatcher.Stop();
        }

        public Uri SnapshotUri { get { return snapshotUri; } }

        public event EventHandler<Utils.StreamWatcher.InfoEvent> OnFrameInfo
        {
            add {
                StartStream();
                streamWatcher.OnFrameInfo += value;
            }
            remove {
                streamWatcher.OnFrameInfo -= value;
                StopStream();
            }
        }

        public event EventHandler<Utils.StreamWatcher.FrameSetEvent> OnFrames
        {
            add {
                StartStream();
                streamWatcher.OnFrames += value;
            }
            remove {
                streamWatcher.OnFrames -= value;
                StopStream();
            }
        }

        private void WorkThread()
        {
            while (!stop.IsSet) {
                try {
                    // Get device link - takes a few moments, as this has to query the device
                    deviceLink = new Onvif.DeviceLink(settings);
                    deviceLink.Start();
                    // Get media link - also has to do some queries
                    mediaLink = deviceLink.GetMedia();
                    mediaLink.Start();
                    Onvif.MediaLink.Profile[] profiles = mediaLink.GetProfiles();
                    var defaultProfile = profiles[0];
                    snapshotUri = mediaLink.GetSnapshotUri(defaultProfile);
                    streamUri = mediaLink.GetStreamUri(defaultProfile);
                }
                catch (Exception e) {
                    Console.WriteLine($"Failed to access camera {settings.Identifier}, retrying shortly: " + e.ToString());
                    Thread.Sleep(1000);
                }
            }
        }

        private void StartStream()
        {
            lock (streamWatcher) {
                if (viewers == 0) {
                    cancellationTokenSource = new CancellationTokenSource();
                    streamTask = StreamAsync(cancellationTokenSource.Token);
                }
                viewers++;
            }
        }

        private void StopStream(bool fin = false)
        {
            lock (streamWatcher) {
                viewers--;
                if (viewers < 0)
                    viewers = 0;
                if (fin || (viewers == 0)) {
                    cancellationTokenSource.Cancel();
                    if (streamTask == null)
                        return;
                    streamTask.Wait();
                    streamTask.Dispose();
                    streamTask = null;
                }
            }
        }

        private NetworkCredential GetCredential()
        {
            if (settings.Credentials == null)
                return new NetworkCredential();
            return new NetworkCredential(settings.Credentials.Username, settings.Credentials.Password);
        }

        private async Task StreamAsync(CancellationToken cancellationToken)
        {
            try {
                TimeSpan delay = TimeSpan.FromSeconds(5);
                while (streamUri == null)
                    await Task.Delay(delay, cancellationToken);
                var parameters = new RtspClientSharp.ConnectionParameters(streamUri, GetCredential());
                using (var rtspClient = new RtspClientSharp.RtspClient(parameters)) {
                    rtspClient.FrameReceived += (sender, frame) => streamWatcher.AddFrame(frame);
                    while (true) {
                        try {
                            await rtspClient.ConnectAsync(cancellationToken);
                        }
                        catch (RtspClientSharp.Rtsp.RtspClientException e) {
                            Console.WriteLine("RTSP client connection error: " + e.ToString());
                            await Task.Delay(delay, cancellationToken);
                            // Restart attempt
                            continue;
                        }
                        try {
                            await rtspClient.ReceiveAsync(cancellationToken);
                        }
                        catch (RtspClientSharp.Rtsp.RtspClientException e) {
                            Console.WriteLine("RTSP client receive error: " + e.ToString());
                            await Task.Delay(delay, cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException) {
                // No need to do anything, this is an expected exception
            }
        }
    }
}
