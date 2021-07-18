using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;
using System.Collections.Concurrent;
using System.Linq;
using TestConsole.Streamer.Recorder.FileFormat;
using static TestConsole.Streamer.Utils.StreamWatcher;

namespace TestConsole.Streamer.Recorder
{
    public class DataFile
    {
        private readonly Configuration.Storage.Container settings;
        private readonly Thread writer;
        private readonly BlockingCollection<FrameData> queue;

        public interface SearchResults
        {
            void VideoStarts(string storageIdentifier, string cameraIdentifier, DateTime timestamp);
            void VideoTag(string storageIdentifier, string cameraIdentifier, DateTime timestamp, string name, byte[] data);
            void VideoData(string storageIdentifier, string cameraIdentifier, byte[][] frameData);
            void VideoStops(string storageIdentifier, string cameraIdentifier, DateTime timestamp);
            void VideoSearched(string storageIdentifier);
        }

        public DataFile(Configuration.Storage.Container container)
        {
            settings = container;
            queue = new BlockingCollection<FrameData>(new ConcurrentQueue<FrameData>());
            writer = new Thread(WorkerThread);
            writer.Name = "Storage writer " + settings.Identifier;
            writer.Start();
        }

        public void Stop()
        {
            queue.Add(null);
            writer.Join();
        }

        private void AddQueue(FrameData data)
        {
            if (data != null)
                queue.Add(data);
        }

        public void SearchTimes(string identifier, DateTime? start, DateTime? end, bool getVideo, SearchResults callback)
        {
            queue.Add(new FrameData(identifier) { Search = new SearchRequest() { Start = start, End = end, Callback = callback, GetVideoData = getVideo } });
        }

        public Streamer.Utils.WebStream.FrameSource GetFrameSourceForRecording(string identifier, DateTime start)
        {
            FrameStreamer streamer = new FrameStreamer(this, identifier, start);
            return streamer;
        }

        public void FinishFrameSet(string identifier, InfoEvent frameSetStart)
        {
            queue.Add(new FrameData(identifier) { RawFrame = frameSetStart });
        }

        public void AddTag(string identifier, string name, byte[] data, DateTime timestamp)
        {
            queue.Add(new FrameData(identifier) { TagInfo = new Tag() { Name = name, Data = data, Timestamp = timestamp } });
        }

        private void WorkerThread()
        {
            RecordingFile file = new RecordingFile(settings);
            Dictionary<string, RecordHeader> recordingCameras = new Dictionary<string, RecordHeader>();
            while (true) {
                var frame = queue.Take();
                if (frame == null)
                    break;
                if (frame.Identifier != null) {
                    RecordHeader currentHeader;
                    if (!recordingCameras.ContainsKey(frame.Identifier)) {
                        currentHeader = new RecordHeader() { Identifier = frame.Identifier };
                        recordingCameras.Add(frame.Identifier, currentHeader);
                    } else {
                        currentHeader = recordingCameras[frame.Identifier];
                    }
                    if (frame.TagInfo != null)
                        currentHeader.Tags.Add(frame.TagInfo);
                    if (frame.RawFrame is FrameSetEvent frameSet) {
                        currentHeader.Tags.Add(new Tag() { Name = "PPS", Timestamp = DateTime.MinValue, Data = frameSet.StreamInfo.Pps });
                        currentHeader.Tags.Add(new Tag() { Name = "SPS", Timestamp = DateTime.MinValue, Data = frameSet.StreamInfo.Sps });
                        currentHeader.FrameData = frameSet.RawFrames;
                        currentHeader.Timestamp = frameSet.StartTimestamp;
                        currentHeader.Duration = frameSet.EndTimestamp - frameSet.StartTimestamp;
                        file.SaveHeader(currentHeader);
                        // Reset our current header
                        recordingCameras[currentHeader.Identifier] = new RecordHeader() { Identifier = currentHeader.Identifier };
                    }
                }
                if (frame.Search != null) {
                    // TODO: Should this be a separate thread?
                    var seeking = file.Oldest;
                    if (frame.Search.Start != null) {
                        while ((seeking != null) && (seeking.Timestamp < frame.Search.Start))
                            seeking = file.GetHeader((UInt32)seeking.NextHeader);
                    }
                    Dictionary<string, DateTime> foundEnds = new Dictionary<string, DateTime>();
                    while (seeking != null) {
                        if ((frame.Search.End != null) && (seeking.Timestamp > frame.Search.End))
                            break;
                        file.LoadData(seeking, RecordHeader.LoadedParts.Identifier | RecordHeader.LoadedParts.Tags);
                        if ((frame.Identifier == null) || frame.Identifier.Equals(seeking.Identifier)) {
                            bool doStart = true;
                            if (foundEnds.ContainsKey(seeking.Identifier)) {
                                DateTime lastTime = foundEnds[seeking.Identifier];
                                if (seeking.Timestamp != lastTime)
                                    frame.Search.Callback.VideoStops(settings.Identifier, seeking.Identifier, lastTime);
                                else
                                    doStart = false;
                            }
                            if (doStart)
                                frame.Search.Callback.VideoStarts(settings.Identifier, seeking.Identifier, seeking.Timestamp);
                            foreach (var tag in seeking.Tags)
                                frame.Search.Callback.VideoTag(settings.Identifier, seeking.Identifier, tag.Timestamp, tag.Name, tag.Data);
                            foundEnds[seeking.Identifier] = seeking.Timestamp + seeking.Duration;
                            if (frame.Search.GetVideoData) {
                                file.LoadData(seeking, RecordHeader.LoadedParts.Frame);
                                frame.Search.Callback.VideoData(settings.Identifier, seeking.Identifier, seeking.FrameData);
                            }
                        }
                        seeking = (seeking.NextHeader == -1) ? null : file.GetHeader((UInt32)seeking.NextHeader);
                    }
                    foreach (KeyValuePair<string, DateTime> kvp in foundEnds)
                        frame.Search.Callback.VideoStops(settings.Identifier, kvp.Key, kvp.Value);
                    frame.Search.Callback.VideoSearched(settings.Identifier);
                }
                if (frame.Streamer != null) {
                    if (frame.Streamer.CurrentHeader == null)
                        frame.Streamer.CurrentHeader = file.Oldest;
                    while ((frame.Streamer.CurrentHeader != null) && ((frame.Streamer.CurrentHeader.Timestamp < frame.Streamer.StartTime) || (frame.Streamer.CurrentHeader.Identifier != frame.Streamer.Identifier)))
                        frame.Streamer.CurrentHeader = file.GetHeader((UInt32)frame.Streamer.CurrentHeader.NextHeader);
                    frame.Streamer.Step();
                }
            }
            file.Stop();
        }

        private class SearchRequest
        {
            public DateTime? Start { get; set; }
            public DateTime? End { get; set; }
            public SearchResults Callback { get; set; }
            public bool GetVideoData { get; set; }
        }

        private class FrameData
        {
            public FrameData(String identifier)
            {
                Identifier = identifier;
            }

            public String Identifier { get; }

            public InfoEvent RawFrame { get; set; }

            public Tag TagInfo { get; set; }

            public SearchRequest Search { get; set; }

            public FrameStreamer Streamer { get; set; }
        }

        public class Tag
        {
            public DateTime Timestamp { get; set; } = DateTime.MinValue;

            public string Name { get; set; }

            public byte[] Data { get; set; }
        }

        public class RecordHeader : Header
        {
            [Flags]
            public enum LoadedParts
            {
                Identifier = 1,
                Tags = 2,
                Frame = 4,
            }

            private byte loadedTagCount;
            private UInt16 loadedDataOffset;
            private byte loadedIdentifierLength;
            private LoadedParts loadedParts;

            public RecordHeader()
            {
                loadedParts = LoadedParts.Frame | LoadedParts.Identifier | LoadedParts.Tags;
            }

            public LoadedParts Loaded { get { return loadedParts; } }

            public String Identifier { get; set; } = "";

            public DateTime Timestamp { get; set; } = DateTime.MinValue;

            public TimeSpan Duration { get; set; } = TimeSpan.Zero;

            public List<Tag> Tags { get; } = new List<Tag>();

            public byte[][] FrameData { get; set; } = new byte[][] { };

            public override uint Length
            {
                get {
                    return base.Length + 20;
                }
            }

            protected override UInt32 ComputeDataSize()
            {
                UInt32 result = (UInt32)(Encoding.UTF8.GetByteCount(Identifier) + (FrameData.Length * 4));
                foreach (var tag in Tags)
                    result += (UInt32)(11 + Encoding.UTF8.GetByteCount(tag.Name) + tag.Data.Length);
                foreach (var data in FrameData)
                    result += (UInt32)data.Length;
                return result;
            }

            protected override void LoadData(BinaryReader reader)
            {
                if (!loadedParts.HasFlag(LoadedParts.Identifier)) {
                    loadedParts |= LoadedParts.Identifier;
                    byte[] identData = reader.ReadBytes(loadedIdentifierLength);
                    Identifier = Encoding.UTF8.GetString(identData);
                } else if (!loadedParts.HasFlag(LoadedParts.Tags)) {
                    loadedParts |= LoadedParts.Tags;
                    reader.BaseStream.Seek(loadedIdentifierLength, SeekOrigin.Current);
                    for (int i = 0; i < loadedTagCount; i++) {
                        DateTime stamp = DateTime.FromBinary(reader.ReadInt64());
                        byte nameLength = reader.ReadByte();
                        UInt16 dataLength = reader.ReadUInt16();
                        byte[] nameData = reader.ReadBytes(nameLength);
                        byte[] tagData = reader.ReadBytes(dataLength);
                        Tags.Add(new Tag() {
                            Name = Encoding.UTF8.GetString(nameData),
                            Data = tagData,
                            Timestamp = stamp,
                        });
                    }
                } else if (!loadedParts.HasFlag(LoadedParts.Frame)) {
                    loadedParts |= LoadedParts.Frame;
                    reader.BaseStream.Seek(loadedDataOffset, SeekOrigin.Current);
                    UInt32 remains = (UInt32)(DataSize - loadedDataOffset);
                    List<byte[]> chunks = new List<byte[]>();
                    while (remains != 0) {
                        UInt32 chunkSize = reader.ReadUInt32();
                        chunks.Add(reader.ReadBytes((Int32)chunkSize));
                        remains -= 4 + chunkSize;
                    }
                    FrameData = chunks.ToArray();
                }
            }

            protected override void LoadExtraHeader(BinaryReader reader)
            {
                loadedTagCount = reader.ReadByte();
                loadedDataOffset = reader.ReadUInt16();
                loadedIdentifierLength = reader.ReadByte();
                Timestamp = DateTime.FromBinary(reader.ReadInt64());
                Duration = TimeSpan.FromTicks(reader.ReadInt64());
                loadedParts = 0;
            }

            protected override void SaveData(BinaryWriter writer)
            {
                // Identifier
                writer.Write(Encoding.UTF8.GetBytes(Identifier));
                // Tags
                foreach (var tag in Tags) {
                    byte[] nameBytes = Encoding.UTF8.GetBytes(tag.Name);
                    writer.Write(tag.Timestamp.ToBinary());
                    writer.Write((byte)nameBytes.Length);
                    writer.Write((UInt16)tag.Data.Length);
                    writer.Write(nameBytes);
                    writer.Write(tag.Data);
                }
                // Data
                foreach (var data in FrameData) {
                    writer.Write((UInt32)data.Length);
                    writer.Write(data);
                }
            }

            protected override void SaveExtraHeader(BinaryWriter writer)
            {
                byte idLength = (byte)(loadedParts.HasFlag(LoadedParts.Identifier) ? Encoding.UTF8.GetByteCount(Identifier) : loadedIdentifierLength);
                UInt16 dataOffset;
                if (loadedParts.HasFlag(LoadedParts.Tags)) {
                    dataOffset = idLength;
                    foreach (var tag in Tags)
                        dataOffset += (UInt16)(11 + Encoding.UTF8.GetByteCount(tag.Name) + tag.Data.Length);
                } else {
                    dataOffset = loadedDataOffset;
                }
                writer.Write((byte)(loadedParts.HasFlag(LoadedParts.Tags) ? Tags.Count : loadedTagCount));
                writer.Write(dataOffset);
                writer.Write((byte)idLength);
                writer.Write(Timestamp.ToBinary());
                writer.Write((Int64)Duration.Ticks);
            }
        }

        internal class RecordingFile : CircularFile<RecordHeader>
        {
            public RecordingFile(Configuration.Storage.Container settings)
                :base(settings.LocalFileName, settings.MaximumSize)
            {
            }

            public void LoadData(RecordHeader header, RecordHeader.LoadedParts type)
            {
                while (!header.Loaded.HasFlag(type))
                    header.LoadData(dataFile);
            }
        }

        internal class FrameStreamer : Utils.WebStream.FrameSource
        {
            private readonly DataFile owner;
            private readonly string identifier;
            private readonly DateTime startTime;
            private readonly Thread reader;
            private readonly BlockingCollection<Boolean> queue;

            public FrameStreamer(DataFile owner, string identifier, DateTime start)
            {
                this.owner = owner;
                this.identifier = identifier;
                startTime = start;
                queue = new BlockingCollection<Boolean>(new ConcurrentQueue<Boolean>());
                reader = new Thread(WorkThread);
                reader.Name = Description;
                reader.Start();
            }

            private void WorkThread()
            {
                do {
                    owner.AddQueue(new FrameData(Identifier) { Streamer = this });
                    if (!queue.Take())
                        break;
                    FrameSetEvent frames = new FrameSetEvent() {
                        StreamInfo = new StreamData(CurrentHeader),
                        StartTimestamp = CurrentHeader.Timestamp,
                        EndTimestamp = CurrentHeader.Timestamp + CurrentHeader.Duration,
                        RawFrames = CurrentHeader.FrameData,
                    };
                    // Everything arrives at once, so invoke both in the correct order
                    OnFrameInfo?.Invoke(this, frames);
                    OnFrames?.Invoke(this, frames);
                } while (CurrentHeader != null);
                // TODO: Close stream
            }

            internal void Step()
            {
                queue.Add(CurrentHeader != null);
            }

            public string Identifier { get { return identifier; } }

            public DateTime StartTime { get { return startTime; } }

            public RecordHeader CurrentHeader { get; set; }

            public string Description
            {
                get {
                    return "Playback " + identifier + " " + startTime.ToString();
                }
            }

            public event EventHandler<InfoEvent> OnFrameInfo;

            public event EventHandler<FrameSetEvent> OnFrames;

            private class StreamData : MP4.Mp4Metadata
            {
                private readonly byte[] sps;
                private readonly byte[] pps;

                public StreamData(RecordHeader header)
                {
                    foreach (var tag in header.Tags) {
                        if (tag.Name == "PPS")
                            pps = tag.Data;
                        else if (tag.Name == "SPS")
                            sps = tag.Data;
                        if ((pps != null) && (sps != null))
                            break;
                    }
                    if (pps == null)
                        pps = new byte[0];
                    if (sps == null)
                        sps = new byte[0];
                }

                public override byte[] Sps => sps;

                public override byte[] Pps => pps;
            }
        }
    }
}
