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
        private class DiscoveryEndpoint : WebServer.IEndpoint
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

            public void Handle(HttpListenerContext request)
            {
                lock (timeoutTimer) {
                    if (token == null)
                        token = discoverer.AddWatcher(new Watcher(devices));
                    timeoutCount = 0;
                    timeoutTimer.Change(0, 1000);
                }
                byte[] result = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(NormalisedData()));
                HttpListenerResponse response = request.Response;
                response.ContentLength64 = result.Length;
                response.ContentType = "text/json";
                response.OutputStream.Write(result, 0, result.Length);
                response.OutputStream.Close();
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
            // TODO: Auto-find these
            server.AddContent("/", WebServer.StaticContent.Load("index.html"));
            server.AddContent("/windows.css", WebServer.StaticContent.Load("windows.css"));
            server.AddContent("/windows.js", WebServer.StaticContent.Load("windows.js"));
            server.AddContent("/discovery", new DiscoveryEndpoint());
            server.Start();
        }

        public WebServer Server { get { return server; } }
    }
}
