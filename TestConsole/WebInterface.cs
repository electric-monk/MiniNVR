using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json;

namespace TestConsole
{
    public class WebInterface
    {
        private abstract class JSONEndpoint : WebServer.IEndpoint
        {
            public abstract void Handle(HttpListenerContext request);

            protected static void Reply(HttpListenerContext request, object data)
            {
                byte[] result = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
                HttpListenerResponse response = request.Response;
                response.ContentLength64 = result.Length;
                response.ContentType = "text/json";
                response.OutputStream.Write(result, 0, result.Length);
                response.OutputStream.Close();
            }
        }

        private class CameraListEndpoint : JSONEndpoint
        {
            public override void Handle(HttpListenerContext request)
            {
                Dictionary<string, Dictionary<string, object>> values = new Dictionary<string, Dictionary<string, object>>();
                foreach (var camera in Configuration.Database.Instance.Cameras.AllCameras) {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties["title"] = camera.FriendlyName;
                    properties["endpoint"] = camera.Endpoint;
                    if (camera.Credentials != null) {
                        properties["username"] = camera.Credentials.Username;
                        properties["password"] = camera.Credentials.Password;
                    }
                    if (camera.StorageIdentifier != null)
                        properties["record"] = camera.StorageIdentifier;
                    values.Add(camera.Identifier, properties);
                }
                Reply(request, values);
            }
        }

        private class CameraUpdateEndpoint : JSONEndpoint
        {
            public override void Handle(HttpListenerContext request)
            {
                string body;
                using (var reader = new System.IO.StreamReader(request.Request.InputStream, request.Request.ContentEncoding))
                    body = reader.ReadToEnd();
                var cameraData = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);

                Configuration.Cameras.Camera camera = new Configuration.Cameras.Camera() {
                    Identifier = cameraData["identifier"],
                    FriendlyName = cameraData["title"],
                    Endpoint = cameraData["endpoint"],
                };
                if ((cameraData["username"].Length != 0) || (cameraData["password"].Length != 0)) {
                    camera.Credentials = new Configuration.Cameras.Camera.CredentialInfo() {
                        Username = cameraData["username"],
                        Password = cameraData["password"],
                    };
                }
                if (cameraData["record"] != "")
                    camera.StorageIdentifier = cameraData["Storage"];
                Configuration.Database.Instance.Cameras.Add(camera);

                Reply(request, true);
            }
        }

        private class StorageListEndpoint : JSONEndpoint
        {
            public override void Handle(HttpListenerContext request)
            {
                Dictionary<string, Dictionary<string, object>> values = new Dictionary<string, Dictionary<string, object>>();
                foreach (var container in Configuration.Database.Instance.Storage.AllContainers) {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties["title"] = container.FriendlyName;
                    properties["filename"] = container.LocalFileName;
                    properties["size"] = container.MaximumSize;
                    values.Add(container.Identifier, properties);
                }
                Reply(request, values);
            }
        }

        private class StorageUpdateEndpoint : JSONEndpoint
        {
            public override void Handle(HttpListenerContext request)
            {
                string body;
                using (var reader = new System.IO.StreamReader(request.Request.InputStream, request.Request.ContentEncoding))
                    body = reader.ReadToEnd();
                var containerData = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);

                Configuration.Storage.Container container = new Configuration.Storage.Container() {
                    Identifier = (string)containerData["identifier"],
                    FriendlyName = (string)containerData["title"],
                    LocalFileName = (string)containerData["filename"],
                    MaximumSize = (UInt64)containerData["size"],
                };
                Configuration.Database.Instance.Storage.Add(container);

                Reply(request, true);
            }
        }

        private class DiscoveryEndpoint : JSONEndpoint
        {
            private readonly Onvif.Discoverer discoverer;
            private readonly List<Onvif.Discoverer.Device> devices;
            private readonly Timer timeoutTimer;
            private object token;
            private int timeoutCount;

            public DiscoveryEndpoint()
            {
                discoverer = new Onvif.Discoverer();
                devices = new List<Onvif.Discoverer.Device>();
                timeoutTimer = new Timer(TimeoutCallback);
            }

            public override void Handle(HttpListenerContext request)
            {
                lock (timeoutTimer) {
                    if (token == null)
                        token = discoverer.AddWatcher(new Watcher(devices));
                    timeoutCount = 0;
                    timeoutTimer.Change(0, 1000);
                }
                Reply(request, NormalisedData());
            }

            private void TimeoutCallback(Object stateInfo)
            {
                lock (timeoutTimer) {
                    timeoutCount++;
                    if (timeoutCount > 15) {
                        timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        if (token != null) { // Should always be true, but for sanity
                            discoverer.RemoveWatcher(token);
                            token = null;
                            devices.Clear();
                        }
                    }
                }
            }

            private Dictionary<string, Dictionary<string, object>> NormalisedData()
            {
                Dictionary<string, Dictionary<string, object>> result = new Dictionary<string, Dictionary<string, object>>();
                foreach (Onvif.Discoverer.Device device in devices) {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties.Add("Manufacturer", device.Manufacturer);
                    properties.Add("Model", device.Model);
                    properties.Add("Types", device.Types.ToList());
                    properties.Add("Endpoints", device.OnvifEndpoints.ToList());
                    result.Add(device.IPAddress, properties);
                }
                return result;
            }

            private class Watcher : Onvif.Discoverer.IListener
            {
                private readonly List<Onvif.Discoverer.Device> devices;

                public Watcher(List<Onvif.Discoverer.Device> d)
                {
                    devices = d;
                }

                public void FoundDevice(Onvif.Discoverer.Device device)
                {
                    devices.Add(device);
                }
            }
        }

        private readonly WebServer server;

        public WebInterface()
        {
            server = new WebServer(12345, 32, 100);
            server.AddContent("/", WebServer.StaticContent.Load("index.html"));
            WebServer.StaticContent.LoadAll("", server);
            server.AddContent("/discovery", new DiscoveryEndpoint());
            server.AddContent("/updateCamera", new CameraUpdateEndpoint());
            server.AddContent("/allCameras", new CameraListEndpoint());
            server.AddContent("/updateStorage", new StorageUpdateEndpoint());
            server.AddContent("/allStorage", new StorageListEndpoint());
            server.Start();
        }

        public WebServer Server { get { return server; } }
    }
}
