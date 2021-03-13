using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TestConsole.Streamer.Utils
{
    public class StreamWatcher
    {
        private readonly BlockingCollection<RtspClientSharp.RawFrames.Video.RawH264Frame> queue;
        private readonly Thread queueThread;
        private readonly List<byte[]> samples;
        private MP4.SpsParser parser;
        private DateTime lastTimestamp;

        internal StreamWatcher()
        {
            samples = new List<byte[]>();
            queue = new BlockingCollection<RtspClientSharp.RawFrames.Video.RawH264Frame>(new ConcurrentQueue<RtspClientSharp.RawFrames.Video.RawH264Frame>());
            queueThread = new Thread(WorkerThread);
            queueThread.Name = "MP4 Receiver";
            queueThread.Start();
        }

        internal void AddFrame(RtspClientSharp.RawFrames.RawFrame frame)
        {
            if (frame is RtspClientSharp.RawFrames.Video.RawH264Frame h264Frame) {
                var safeFrame = CopyFrame(h264Frame);
                if (safeFrame != null)
                    queue.Add(safeFrame);
            }
        }

        internal void Stop()
        {
            queue.Add(null);
            queue.CompleteAdding();
            queueThread.Join();
            samples.Clear();
            parser = null;
        }

        public class InfoEvent : EventArgs
        {
            public MP4.Mp4Metadata StreamInfo { get; set; }

            public DateTime StartTimestamp { get; set; }
        }

        public class FrameSetEvent : InfoEvent
        {
            // Pre-processed frames, ready for insertion into e.g. an MP4 file
            public byte[][] RawFrames { get; set; }

            public DateTime EndTimestamp { get; set; }
        }

        internal EventHandler<InfoEvent> OnFrameInfo;

        internal EventHandler<FrameSetEvent> OnFrames;

        private void WorkerThread()
        {
            while (true) {
                var frame = queue.Take();
                if (frame == null)
                    break;
                HandleFrame(frame);
            }
        }

        private void HandleFrame(RtspClientSharp.RawFrames.Video.RawH264Frame frame)
        {
            bool isIFrame = false;
            if (frame is RtspClientSharp.RawFrames.Video.RawH264IFrame iframe) {
                isIFrame = true;
                if (parser != null) {
                    OnFrames?.Invoke(this, new FrameSetEvent { StartTimestamp = lastTimestamp, EndTimestamp = iframe.Timestamp, StreamInfo = parser, RawFrames = samples.ToArray() });
                    samples.Clear();
                }
                lastTimestamp = iframe.Timestamp;
                parser = new MP4.SpsParser(iframe.SpsPpsSegment.ToArray());
                OnFrameInfo?.Invoke(this, new InfoEvent { StartTimestamp = lastTimestamp, StreamInfo = parser });
            }
            if (parser != null) {
                byte[] sample = frame.FrameSegment.ToArray();
                byte[][] parts;
                if (isIFrame) {
                    // Apparently, I frame should also be preceded by SPS and PPS, even though they're also in the header.
                    parts = new byte[][] { parser.Sps, parser.Pps, sample };
                } else {
                    // Check it has a start marker (as we use it to update it to a length marker)
                    parts = new byte[][] { sample };
                }
                samples.Add(AppendDataWithStartMarker(parts));
            }
        }

        private static ArraySegment<byte> SaveBuffer(ArraySegment<byte> buffer)
        {
            return new ArraySegment<byte>(buffer.ToArray());
        }

        public static RtspClientSharp.RawFrames.Video.RawH264Frame CopyFrame(RtspClientSharp.RawFrames.Video.RawH264Frame frame)
        {
            RtspClientSharp.RawFrames.Video.RawH264Frame duplicate = null;
            if (frame is RtspClientSharp.RawFrames.Video.RawH264IFrame iFrame) {
                duplicate = new RtspClientSharp.RawFrames.Video.RawH264IFrame(iFrame.Timestamp, SaveBuffer(iFrame.FrameSegment), SaveBuffer(iFrame.SpsPpsSegment));
            } else if (frame is RtspClientSharp.RawFrames.Video.RawH264PFrame pFrame) {
                duplicate = new RtspClientSharp.RawFrames.Video.RawH264PFrame(pFrame.Timestamp, SaveBuffer(pFrame.FrameSegment));
            }
            return duplicate;
        }

        private static byte[] AppendDataWithStartMarker(byte[][] data)
        {
            int total = 0;
            int i = 0;
            bool[] prepend = new bool[data.Length];
            foreach (var part in data) {
                prepend[i] = MP4.SpsParser.FindPattern(part, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, 0) != 0;
                if (prepend[i])
                    total += RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length;
                total += part.Length;
                i++;
            }
            if ((prepend.Length == 1) && !prepend[0])
                return data[0];
            byte[] result = new byte[total];
            int j = 0, k = 0;
            foreach (var part in data) {
                if (prepend[k]) {
                    Buffer.BlockCopy(RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, 0, result, j, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length);
                    j += RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length;
                }
                Buffer.BlockCopy(part, 0, result, j, part.Length);
                j += part.Length;
                k++;
            }
            return result;
        }
    }
}
