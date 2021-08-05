using System;
using System.Collections.Concurrent;
using System.Net;
using System.IO;
using System.Threading;

namespace TestConsole.Streamer.Utils
{
    public class WebStream : WebServer.IEndpoint
    {
        public interface FrameSource {
            event EventHandler<Utils.StreamWatcher.InfoEvent> OnFrameInfo;
            event EventHandler<Utils.StreamWatcher.FrameSetEvent> OnFrames;
            string Description { get; }
        }

        public interface StreamSource
        {
            FrameSource SourceForRequest(HttpListenerRequest request);
        }

        private StreamSource streamSource;

        public WebStream(StreamSource source)
        {
            streamSource = source;
        }

        public void Handle(HttpListenerContext request)
        {
            // TODO: Check credentials
            new Streamer(streamSource.SourceForRequest(request.Request), request.Response);
        }

        private class Streamer
        {
            // TODO: If the camera is deleted before the stream starts, will this ever notice, or just leak?

            private readonly FrameSource frameSource;
            private readonly BlockingCollection<StreamWatcher.InfoEvent> queue;
            private readonly Thread thread;
            private readonly HttpListenerResponse response;

            public Streamer(FrameSource source, HttpListenerResponse response)
            {
                frameSource = source;
                queue = new BlockingCollection<StreamWatcher.InfoEvent>(new ConcurrentQueue<StreamWatcher.InfoEvent>());
                thread = new Thread(WorkerThread);
                thread.Name = "Web Streamer " + frameSource.Description;
                response.ContentType = "video/mp4";
                response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                response.AddHeader("Pragma", "no-cache");
                response.AddHeader("Expires", "0");
                this.response = response;
                thread.Start();
                frameSource.OnFrameInfo += HandleFrameInfo;
            }

            private void HandleFrameInfo(object sender, StreamWatcher.InfoEvent info)
            {
                queue.Add(info);
                frameSource.OnFrames += HandleFrames;
                frameSource.OnFrameInfo -= HandleFrameInfo;
            }

            private void HandleFrames(object sender, StreamWatcher.FrameSetEvent frames)
            {
                queue.Add(frames);
            }

            private void WorkerThread()
            {
                try {
                    MP4.Mp4Helper mp4File = new MP4.Mp4Helper();
                    bool started = false;
                    StreamWatcher.InfoEvent info;
                    while ((info = queue.Take()) != null) {
                        if (info == null)
                            break;
                        try {
                            if (!started) {
                                started = true;
                                // Set a header indicating the MIME type string that Media Streaming Extensions require, which contains info the file will also feed them
                                response.AddHeader("X-MSE-Codec", info.StreamInfo.MSEMimeType);
                                // After this point, headers will no longer be possible
                                mp4File.CreateEmptyMP4(info.StreamInfo).SaveTo(response.OutputStream);
                            }
                            if (info is StreamWatcher.FrameSetEvent frames) {
                                var boxes = mp4File.CreateChunk(frames.RawFrames);
                                foreach (var box in boxes)
                                    box.ToStream(response.OutputStream);
                            }
                        }
                        catch (HttpListenerException) {
                            // This means the client dropped, which is expected and means we just need to tidy up
                            break;
                        }
                        catch (System.IO.IOException) {
                            // This means the client dropped, but on mono, which generates the wrong exceptions in HttpListener
                            break;
                        }
                    }
                }
                finally {
                    frameSource.OnFrames -= HandleFrames;
                    response.OutputStream.Close();
                }
            }
        }
    }
}
