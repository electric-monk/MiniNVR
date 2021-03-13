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
using TestConsole.Streamer.Utils;

namespace TestConsole
{
    public class Program
    {
        private class Server
        {
            private readonly WebInterface web = new WebInterface();
            private readonly Streamer.StorageManager storageManager = new Streamer.StorageManager();
            private readonly Streamer.CameraManager cameraManager;

            public Server()
            {
                cameraManager = new Streamer.CameraManager(storageManager);

                Configuration.Database.Instance.Cameras.OnUpdated += OnCameraChanged;
                foreach (var cam in Configuration.Database.Instance.Cameras.AllCameras)
                    OnCameraChanged(this, new Configuration.ConfigurableList<Configuration.Cameras.Camera>.UpdateEvent() { AddedUpdated = true, ChangedItem = cam });
            }

            private void OnCameraChanged(object sender, Configuration.ConfigurableList<Configuration.Cameras.Camera>.UpdateEvent change)
            {
                WebServer.IEndpoint endpoint = null;
                if (change.AddedUpdated)
                    endpoint = new Streamer.Utils.WebStream(new CameraWrapper(cameraManager.GetCamera(change.ChangedItem.Identifier)));
                web.Server.AddContent("/stream-" + change.ChangedItem.Identifier, endpoint);
            }

            private class CameraWrapper : Streamer.Utils.WebStream.StreamSource, Streamer.Utils.WebStream.FrameSource
            {
                private readonly Streamer.Camera camera;

                public CameraWrapper(Streamer.Camera camera)
                {
                    this.camera = camera;
                }

                public WebStream.FrameSource SourceForRequest(HttpListenerRequest request)
                {
                    return this;
                }

                public string Description
                {
                    get {
                        return camera.Identifier;
                    }
                }

                public event EventHandler<StreamWatcher.InfoEvent> OnFrameInfo
                {
                    add {
                        camera.OnFrameInfo += value;
                    }
                    remove {
                        camera.OnFrameInfo -= value;
                    }
                }

                public event EventHandler<StreamWatcher.FrameSetEvent> OnFrames
                {
                    add {
                        camera.OnFrames += value;
                    }
                    remove {
                        camera.OnFrames -= value;
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            Server server = new Server();
            Console.WriteLine("Server up");
            // TODO: some sort of shutdown strategy
            while (true)
                Thread.Sleep(10000000);
        }
    }
}
