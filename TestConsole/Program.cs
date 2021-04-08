using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Net;
using TestConsole.Streamer.Utils;
using Newtonsoft.Json;

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
                web.Server.AddContent("/storage", new StorageWrapper(storageManager));

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

            private class StorageWrapper : WebInterface.JSONEndpoint
            {
                private readonly Streamer.StorageManager storageManager;

                public StorageWrapper(Streamer.StorageManager manager)
                {
                    storageManager = manager;
                }

                public override void Handle(HttpListenerContext request)
                {
                    new ResultResponder(storageManager, request);
                }

                private class ResultResponder : Streamer.Recorder.DataFile.SearchResults
                {
                    private readonly Streamer.StorageManager storageManager;
                    private readonly BlockingCollection<Item> queue;
                    private readonly HttpListenerContext context;
                    private readonly Thread thread;

                    public ResultResponder(Streamer.StorageManager manager, HttpListenerContext request)
                    {
                        storageManager = manager;
                        context = request;
                        queue = new BlockingCollection<Item>(new ConcurrentQueue<Item>());
                        thread = new Thread(WorkerThread);
                        thread.Name = "RecordResponder";
                        thread.Start();
                    }

                    private void WorkerThread()
                    {
                        ItemState state = InitiateRequests(context.Request);
                        while (state.expecting != 0)
                            queue.Take().Update(ref state);
                        TidyUp(state);
                        GenerateResponse(state);
                        queue.CompleteAdding();
                    }

                    private ItemState InitiateRequests(HttpListenerRequest request)
                    {
                        // TODO: Check credentials
                        string body;
                        using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                            body = reader.ReadToEnd();
                        var requestData = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);

                        string storageIdentifier;
                        if ((requestData == null) || !requestData.TryGetValue("storage", out storageIdentifier))
                            storageIdentifier = null;

                        string cameraIdentifier;
                        if ((requestData == null) || !requestData.TryGetValue("camera", out cameraIdentifier))
                            cameraIdentifier = null;

                        DateTime? start;
                        string startStr;
                        if ((requestData != null) && requestData.TryGetValue("start", out startStr)) {
                            start = DateTime.Parse(startStr);
                        } else {
                            start = null;
                        }

                        DateTime? end;
                        string endStr;
                        if ((requestData != null) && requestData.TryGetValue("end", out endStr)) {
                            end = DateTime.Parse(endStr);
                        } else {
                            end = null;
                        }

                        Configuration.Storage.Container[] searchContainers = Configuration.Database.Instance.Storage.AllContainers;
                        if (storageIdentifier != null) {
                            Configuration.Storage.Container found = null;
                            foreach (var container in searchContainers) {
                                if (container.Identifier.Equals(storageIdentifier)) {
                                    found = container;
                                    break;
                                }
                            }
                            if (found == null)
                                searchContainers = new Configuration.Storage.Container[0];
                            else
                                searchContainers = new Configuration.Storage.Container[] { found };
                        }

                        ItemState result = new ItemState();
                        foreach (var container in searchContainers) {
                            var storage = storageManager.GetStorage(container.Identifier);
                            if (storage != null) {
                                storage.SearchTimes(cameraIdentifier, start, end, this);
                                result.expecting++;
                            }
                        }
                        return result;
                    }

                    private void TidyUp(ItemState state)
                    {
                        foreach (var track in state.tracks.Values) {
                            // Sort by time
                            track.Sort((TimeItem a, TimeItem b) => a.Timestamp.CompareTo(b.Timestamp));
                            // Remove stop/start pairs that are at the same time
                            // TODO: Confirm I even need to do this
                            List<int> toRemove = new List<int>();
                            for (int i = 0; i < (track.Count - 1); i++) {
                                if (!(track[i] is Stop))
                                    continue;
                                if (!(track[i + 1] is Start))
                                    continue;
                                if (track[i].SimilarTime(track[i + 1])) {
                                    toRemove.Insert(0, i);
                                    i++;
                                }
                            }
                            foreach (int i in toRemove)
                                track.RemoveRange(i, 2);
                        }
                    }

                    private void GenerateResponse(ItemState state)
                    {
                        Dictionary<string, List<Dictionary<string, object>>> results = new Dictionary<string, List<Dictionary<string, object>>>();
                        foreach (var track in state.tracks) {
                            List<Dictionary<string, object>> data = new List<Dictionary<string, object>>();
                            foreach (var entry in track.Value)
                                data.Add(entry.Generate());
                            results.Add(track.Key, data);
                        }
                        Reply(context, results);
                    }

                    public void VideoStarts(string storageIdentifier, string cameraIdentifier, DateTime timestamp)
                    {
                        queue.Add(new Start() { StorageIdentifier = storageIdentifier, CameraIdentifier = cameraIdentifier, Timestamp = timestamp });
                    }

                    public void VideoTag(string storageIdentifier, string cameraIdentifier, DateTime timestamp, string name, byte[] data)
                    {
                        if (timestamp != DateTime.MinValue)
                            queue.Add(new Tag() { StorageIdentifier = storageIdentifier, CameraIdentifier = cameraIdentifier, Timestamp = timestamp, Name = name, Data = data });
                    }

                    public void VideoStops(string storageIdentifier, string cameraIdentifier, DateTime timestamp)
                    {
                        queue.Add(new Stop() { StorageIdentifier = storageIdentifier, CameraIdentifier = cameraIdentifier, Timestamp = timestamp });
                    }

                    public void VideoSearched(string storageIdentifier)
                    {
                        queue.Add(new Done() { StorageIdentifier = storageIdentifier });
                    }

                    private class ItemState
                    {
                        public int expecting = 0;
                        public readonly Dictionary<string, List<TimeItem>> tracks = new Dictionary<string, List<TimeItem>>();
                    }
                    private abstract class Item
                    {
                        public string StorageIdentifier { get; set; }
                        public abstract void Update(ref ItemState state);
                    }
                    private abstract class TimeItem : Item
                    {
                        private readonly string type;
                        protected TimeItem(string typeName)
                        {
                            type = typeName;
                        }
                        public DateTime Timestamp { get; set; }
                        public string CameraIdentifier { get; set; }
                        public override void Update(ref ItemState state)
                        {
                            if (!state.tracks.ContainsKey(CameraIdentifier))
                                state.tracks.Add(CameraIdentifier, new List<TimeItem>());
                            state.tracks[CameraIdentifier].Add(this);
                        }
                        public bool SimilarTime(TimeItem other)
                        {
                            return Timestamp.Subtract(other.Timestamp).Duration().TotalSeconds < 1;
                        }
                        public virtual Dictionary<string, object> Generate()
                        {
                            Dictionary<string, object> result = new Dictionary<string, object>();
                            result.Add("type", type);
                            result.Add("timestamp", Timestamp);
                            return result;
                        }
                    }
                    private class Start : TimeItem
                    {
                        public Start() : base("start") { }
                        public override Dictionary<string, object> Generate()
                        {
                            var result = base.Generate();
                            result.Add("storage", StorageIdentifier);
                            return result;
                        }
                    }
                    private class Tag : TimeItem
                    {
                        public Tag() : base("tag") { }
                        public string Name { get; set; }
                        public byte[] Data { get; set; }
                        public override Dictionary<string, object> Generate()
                        {
                            var result = base.Generate();
                            result.Add("name", Name);
                            result.Add("data", Data);
                            return result;
                        }
                    }
                    private class Stop : TimeItem
                    {
                        public Stop() : base("stop") { }
                    }
                    private class Done : Item
                    {
                        public override void Update(ref ItemState state)
                        {
                            state.expecting--;
                        }
                    }
                }
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
