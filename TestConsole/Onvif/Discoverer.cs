using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestConsole.Onvif
{
    public class Discoverer : IDisposable
    {
        private readonly Dictionary<int, IListener> listeners;
        private readonly List<Device> currentDevices;
        private CancellationTokenSource cancellation;

        public class Device : IEquatable<Device>
        {
            public Device(string mfg, string md, string ip, string[] types, string[] endpoints)
            {
                Manufacturer = mfg;
                Model = md;
                IPAddress = ip;
                Types = types;
                OnvifEndpoints = endpoints;
            }

            public string Manufacturer { get; }

            public string Model { get; }

            public string IPAddress { get; }

            public string[] Types { get; }

            public string[] OnvifEndpoints { get; }

            public bool Equals(Device other)
            {
                return IPAddress == other.IPAddress;
            }
        }

        public interface IListener
        {
            void FoundDevice(Device device);
        }

        public Discoverer()
        {
            listeners = new Dictionary<int, IListener>();
            currentDevices = new List<Device>();
        }

        private void ResetSearch()
        {
            if (cancellation != null) {
                listeners.Clear();
                cancellation.Cancel();
                cancellation = null;
                currentDevices.Clear();
            }
        }

        public void Dispose()
        {
            lock (listeners) {
                ResetSearch();
            }
        }

        public object AddWatcher(IListener listener)
        {
            lock (listeners) {
                bool startTask = listeners.Count == 0;
                int token = startTask ? 0 : listeners.Keys.Max() + 1;
                listeners.Add(token, listener);
                if (cancellation == null) {
                    cancellation = new CancellationTokenSource();
                    _ = DiscoverAsync(cancellation.Token);
                }
                foreach (Device device in currentDevices)
                    listener.FoundDevice(device);
                return token;
            }
        }

        public void RemoveWatcher(object token)
        {
            lock (listeners) {
                listeners.Remove((int)token);
                if (listeners.Count == 0)
                    ResetSearch();
            }
        }

        private async Task DiscoverAsync(CancellationToken token)
        {
            var discovery = new OnvifDiscovery.Discovery();
            while (!token.IsCancellationRequested) {
                await discovery.Discover(30, (d) => {
                    Device device = new Device(d.Mfr, d.Model, d.Address, d.Types.ToArray(), d.XAdresses.ToArray());
                    lock (listeners) {
                        if (!currentDevices.Contains(device)) {
                            currentDevices.Add(device);
                            foreach (IListener listener in listeners.Values)
                                listener.FoundDevice(device);
                        }
                    }
                }, token);
            }
        }
    }
}
