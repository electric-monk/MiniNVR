using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.IO;
using System.Threading;

namespace TestConsole.Streamer.Utils
{
    public class WebStream : WebServer.IEndpoint
    {
        private Camera camera;

        public WebStream(Camera source)
        {
            camera = source;
        }

        public void Handle(HttpListenerContext request)
        {
            // TODO: Check credentials
            new Streamer(camera, request.Response);
        }

        private class Streamer
        {
            // TODO: If the camera is deleted before the stream starts, will this ever notice, or just leak?

            private readonly Camera camera;
            private readonly BlockingCollection<StreamWatcher.InfoEvent> queue;
            private readonly Thread thread;
            private readonly Stream stream;

            public Streamer(Camera camera, HttpListenerResponse response)
            {
                this.camera = camera;
                queue = new BlockingCollection<StreamWatcher.InfoEvent>(new ConcurrentQueue<StreamWatcher.InfoEvent>());
                thread = new Thread(WorkerThread);
                thread.Name = "Web Streamer " + camera.Identifier;
                response.ContentType = "video/mp4";
                response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                response.AddHeader("Pragma", "no-cache");
                response.AddHeader("Expires", "0");
                stream = response.OutputStream;
                thread.Start();
                camera.OnFrameInfo += HandleFrameInfo;
            }

            private void HandleFrameInfo(object sender, StreamWatcher.InfoEvent info)
            {
                queue.Add(info);
                camera.OnFrames += HandleFrames;
                camera.OnFrameInfo -= HandleFrameInfo;
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
                                mp4File.CreateEmptyMP4(info.StreamInfo).SaveTo(stream);
                            }
                            if (info is StreamWatcher.FrameSetEvent frames) {
                                var boxes = mp4File.CreateChunk(frames.RawFrames);
                                foreach (var box in boxes)
                                    box.ToStream(stream);
                            }
                        }
                        catch (HttpListenerException) {
                            // This means the client dropped, which is expected and means we just need to tidy up
                            break;
                        }
                    }
                }
                finally {
                    camera.OnFrames -= HandleFrames;
                    stream.Close();
                }
            }
        }
    }
}
