using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
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
                        try {
                            while (state.expecting != 0)
                                queue.Take().Update(ref state);
                        }
                        finally {
                            state.TidyUp();
                            state.GenerateResponse();
                            queue.CompleteAdding();
                        }
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

                        bool getStream;
                        string videoStr;
                        if ((requestData != null) && requestData.TryGetValue("video", out videoStr)) {
                            getStream = bool.Parse(videoStr);
                        } else {
                            getStream = false;
                        }

                        if (getStream && ((start == null) || (end == null) || (storageIdentifier == null) || (cameraIdentifier == null))) {
                            throw new ArgumentException("Start and end times and storage and camera identifiers are all required to produce a video stream");
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

                        if (getStream && (searchContainers.Length == 0)) {
                            throw new ArgumentException("No video found");
                        }

                        ItemState result;
                        if (getStream)
                            result = new StreamItemState(context);
                        else
                            result = new SearchItemState(context);
                        foreach (var container in searchContainers) {
                            var storage = storageManager.GetStorage(container.Identifier);
                            if (storage != null) {
                                storage.SearchTimes(cameraIdentifier, start, end, getStream, this);
                                result.expecting++;
                            }
                        }
                        return result;
                    }

                    public void VideoStarts(string storageIdentifier, string cameraIdentifier, DateTime timestamp)
                    {
                        queue.Add(new Start() { StorageIdentifier = storageIdentifier, CameraIdentifier = cameraIdentifier, Timestamp = timestamp });
                    }

                    public void VideoTag(string storageIdentifier, string cameraIdentifier, DateTime timestamp, string name, byte[] data)
                    {
                        queue.Add(new Tag() { StorageIdentifier = storageIdentifier, CameraIdentifier = cameraIdentifier, Timestamp = timestamp, Name = name, Data = data });
                    }

                    public void VideoStops(string storageIdentifier, string cameraIdentifier, DateTime timestamp)
                    {
                        queue.Add(new Stop() { StorageIdentifier = storageIdentifier, CameraIdentifier = cameraIdentifier, Timestamp = timestamp });
                    }

                    public void VideoData(string storageIdentifier, string cameraIdentifier, byte[][] data)
                    {
                        queue.Add(new Data() { StorageIdentifier = storageIdentifier, CameraIdentifier = cameraIdentifier, FrameData = data });
                    }

                    public void VideoSearched(string storageIdentifier)
                    {
                        queue.Add(new Done() { StorageIdentifier = storageIdentifier });
                    }

                    private abstract class ItemState
                    {
                        protected readonly HttpListenerContext context;

                        protected ItemState(HttpListenerContext context, string contentType)
                        {
                            this.context = context;
                            context.Response.ContentType = contentType;
                        }

                        public int expecting = 0;
                        public abstract void Update(TimeItem item);
                        public virtual void TidyUp() { }
                        public abstract void GenerateResponse();
                        public virtual void OnFrameData(byte[][] data) { }
                    }

                    private class StreamItemState : ItemState
                    {
                        private readonly MP4.Mp4Helper mp4Helper = new MP4.Mp4Helper();
                        private StreamDataMaker maker = new StreamDataMaker();
                        public StreamItemState(HttpListenerContext context)
                        : base(context, "video/mp4")
                        {
                        }
                        public override void Update(TimeItem item)
                        {
                            if (maker != null) {
                                if (item is Start start) {
                                    String jsonDate = JsonConvert.SerializeObject(start.Timestamp);
                                    context.Response.AddHeader("X-StartTime", jsonDate.Substring(1, jsonDate.Length - 2));
                                } else if (item is Tag tag) {
                                    maker.Add(tag);
                                    if (maker.Complete) {
                                        // For MSE, generate a MIME type
                                        context.Response.AddHeader("X-MSE-Codec", maker.MSEMimeType);
                                        // Continue as usual, no more headers
                                        mp4Helper.CreateEmptyMP4(maker).SaveTo(context.Response.OutputStream);
                                        maker = null;
                                    }
                                }
                            }
                        }
                        public override void GenerateResponse()
                        {
                            context.Response.Close();
                        }
                        public override void OnFrameData(byte[][] data)
                        {
                            if (maker == null) {
                                var boxes = mp4Helper.CreateChunk(data);
                                foreach (var box in boxes)
                                    box.ToStream(context.Response.OutputStream);
                            }
                        }

                        private class StreamDataMaker : MP4.Mp4Metadata
                        {
                            private byte[] sps = null;
                            private byte[] pps = null;

                            public void Add(Tag tag)
                            {
                                if (tag.Name == "PPS")
                                    pps = tag.Data;
                                else if (tag.Name == "SPS")
                                    sps = tag.Data;
                            }

                            public bool Complete => (pps != null) && (sps != null);

                            public override byte[] Sps => sps;

                            public override byte[] Pps => pps;
                        }
                    }

                    private class SearchItemState : ItemState
                    {
                        public readonly Dictionary<string, List<TimeItem>> tracks = new Dictionary<string, List<TimeItem>>();

                        public SearchItemState(HttpListenerContext context) : base(context, "application/json") { }

                        public override void Update(TimeItem item)
                        {
                            if (item is Tag tag)
                                if (tag.Timestamp == DateTime.MinValue)
                                    return;
                            if (!tracks.ContainsKey(item.CameraIdentifier))
                                tracks.Add(item.CameraIdentifier, new List<TimeItem>());
                            tracks[item.CameraIdentifier].Add(item);
                        }

                        public override void TidyUp()
                        {
                            foreach (var track in tracks.Values) {
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

                        public override void GenerateResponse()
                        {
                            Dictionary<string, List<Dictionary<string, object>>> results = new Dictionary<string, List<Dictionary<string, object>>>();
                            foreach (var track in tracks) {
                                List<Dictionary<string, object>> data = new List<Dictionary<string, object>>();
                                foreach (var entry in track.Value)
                                    data.Add(entry.Generate());
                                results.Add(track.Key, data);
                            }
                            Reply(context, results);
                        }
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
                            state.Update(this);
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
                    private class Data : Item
                    {
                        public Data() : base() { }
                        public string CameraIdentifier { get; set; }
                        public byte[][] FrameData { get; set; }
                        public override void Update(ref ItemState state)
                        {
                            state.OnFrameData(FrameData);
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
