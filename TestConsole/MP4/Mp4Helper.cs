using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MatrixIO.IO;
using System.Collections;
using MatrixIO.IO.Bmff;

namespace TestConsole.MP4
{
    class Mp4Helper
    {
        private static class HDLR
        {
            public static readonly string TYPE_VIDEO = "vide";
            public static readonly string NAME_VIDEO = "VideoHandler";
            public static readonly string TYPE_AUDIO = "soun";
            public static readonly string NAME_AUDIO = "SoundHandler";
            public static readonly string TYPE_SUBTITLE = "clcp";
            public static readonly string NAME_SUBTITLE = "ClosedCaptionHandler";
        }

        [MatrixIO.IO.Bmff.Box("avc1", "AVC1 Samples Box")]
        public class Avc1 : MatrixIO.IO.Bmff.Box, MatrixIO.IO.Bmff.ISuperBox
        {
            public Avc1()
                : base()
            {
                HorzDPI = 0x480000; // 72dpi
                VertDPI = 0x480000; // 72dpi
                FrameCount = 1;
            }

            public Avc1(System.IO.Stream stream) : base(stream) { }

            public UInt16 CodecStreamVersion { get; set; }

            public UInt16 CodecStreamRevision { get; set; }

            public UInt16 VideoWidth { get; set; }

            public UInt16 VideoHeight { get; set; }

            public UInt32 HorzDPI { get; set; }

            public UInt32 VertDPI { get; set; }

            public UInt32 DataSize { get; set; }

            public UInt16 FrameCount { get; set; }

            public String CompressorName { get; set; }

            public override ulong CalculateSize()
            {
                return base.CalculateSize() + 78;
            }

            protected override void LoadFromStream(System.IO.Stream stream)
            {
                base.LoadFromStream(stream);
                stream.ReadBEUInt32();
                stream.ReadBEUInt16();
                stream.ReadBEUInt16();
                CodecStreamVersion = stream.ReadBEUInt16();   // 2 == uncompressed YCbCr
                CodecStreamRevision = stream.ReadBEUInt16();
                stream.ReadBEUInt32();    // Reserved
                stream.ReadBEUInt32();    // Reserved
                stream.ReadBEUInt32();    // Reserved
                VideoWidth = stream.ReadBEUInt16();
                VideoHeight = stream.ReadBEUInt16();
                HorzDPI = stream.ReadBEUInt32();
                VertDPI = stream.ReadBEUInt32();
                DataSize = stream.ReadBEUInt32();
                FrameCount = stream.ReadBEUInt16();
                byte count = (byte)stream.ReadByte();
                if (count > 31)
                    count = 0;
                if (count == 0) {
                    CompressorName = null;
                } else {
                    byte[] data = stream.ReadBytes(count);
                    CompressorName = Encoding.UTF8.GetString(data);
                }
                if (count != 31)
                    stream.ReadBytes(31 - count);
                stream.ReadBEUInt16(); // Depth?
                stream.ReadBEUInt16();   // pre_defined?
            }

            protected override void SaveToStream(System.IO.Stream stream)
            {
                base.SaveToStream(stream);
                stream.WriteBEUInt32(0);    // Reserved
                stream.WriteBEUInt16(0);    // Reserved
                stream.WriteBEUInt16(1);    // Data reference index
                stream.WriteBEUInt16(CodecStreamVersion);   // 2 == uncompressed YCbCr
                stream.WriteBEUInt16(CodecStreamRevision);
                stream.WriteBEUInt32(0);    // Reserved
                stream.WriteBEUInt32(0);    // Reserved
                stream.WriteBEUInt32(0);    // Reserved
                stream.WriteBEUInt16(VideoWidth);
                stream.WriteBEUInt16(VideoHeight);
                stream.WriteBEUInt32(HorzDPI);
                stream.WriteBEUInt32(VertDPI);
                stream.WriteBEUInt32(DataSize);
                stream.WriteBEUInt16(FrameCount);
                if (CompressorName == null) {
                    stream.WriteBytes(new byte[32]);    // Compressor name
                } else {
                    byte[] data = Encoding.UTF8.GetBytes(CompressorName);
                    byte count = (byte)Math.Min(data.Length, 31);
                    stream.WriteByte(count);
                    stream.Write(data, 0, count);
                    if (count < 31)
                        stream.WriteBytes(new byte[31 - count]);
                }
                stream.WriteBEUInt16(0x18); // Depth?
                stream.WriteBEUInt16(0xFFFF);   // pre_defined?
            }

            public IList<MatrixIO.IO.Bmff.Box> Children { get; } = new List<MatrixIO.IO.Bmff.Box>();

            public IEnumerator<MatrixIO.IO.Bmff.Box> GetEnumerator() => Children.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => Children.GetEnumerator();

        }

        [MatrixIO.IO.Bmff.Box("avcC", "SPS/PPS data")]
        public class AvcC : MatrixIO.IO.Bmff.Box
        {
            public AvcC()
                : base()
            {
            }

            public AvcC(System.IO.Stream stream) : base(stream) { }

            public byte ConfigurationVersion { get; set; } = 1;

            public byte ProfileIndication { get; set; }

            public byte Compatibility { get; set; }

            public byte LevelIndication { get; set; }

            public byte[][] SequenceParameterSets { get; set; } = new byte[0][];

            public byte[][] PictureParameterSets { get; set; } = new byte[0][];

            public byte ChromaFormatIdc { get; set; }

            public byte BitDepthLuma { get; set; }

            public byte BitDepthChroma { get; set; }

            public byte[][] SequenceParameterSetsExt { get; set; } = new byte[0][];

            public override ulong CalculateSize()
            {
                ulong size = base.CalculateSize() + 7;
                foreach (byte[] item in SequenceParameterSets)
                    size += (ulong)item.Length + 2;
                foreach (byte[] item in PictureParameterSets)
                    size += (ulong)item.Length + 2;
                if (ProfileHasExtraData(ProfileIndication)) {
                    size += 4;
                    foreach (byte[] item in SequenceParameterSetsExt)
                        size += 2 + (ulong)item.Length;
                }
                return size;
            }

            protected override void LoadFromStream(System.IO.Stream stream)
            {
                base.LoadFromStream(stream);
                ConfigurationVersion = (byte)stream.ReadByte();
                ProfileIndication = (byte)stream.ReadByte();
                Compatibility = (byte)stream.ReadByte();
                LevelIndication = (byte)stream.ReadByte();
                stream.ReadByte(); // Reserved
                SequenceParameterSets = new byte[stream.ReadByte() & ~0xE0][];
                for (int i = 0; i < SequenceParameterSets.Length; i++)
                    SequenceParameterSets[i] = stream.ReadBytes(stream.ReadBEUInt16());
                PictureParameterSets = new byte[stream.ReadByte()][];
                for (int i = 0; i < PictureParameterSets.Length; i++)
                    PictureParameterSets[i] = stream.ReadBytes(stream.ReadBEUInt16());
                if (ProfileHasExtraData(ProfileIndication)) {
                    ChromaFormatIdc = (byte)(stream.ReadByte() & ~0xfc);
                    BitDepthLuma = (byte)(stream.ReadByte() & ~0xf8);
                    BitDepthChroma = (byte)(stream.ReadByte() & ~0xf8);
                    SequenceParameterSetsExt = new byte[stream.ReadByte()][];
                    for (int i = 0; i < SequenceParameterSetsExt.Length; i++)
                        SequenceParameterSetsExt[i] = stream.ReadBytes(stream.ReadBEUInt16());
                }
            }

            protected override void SaveToStream(System.IO.Stream stream)
            {
                base.SaveToStream(stream);
                stream.WriteByte(ConfigurationVersion);
                stream.WriteByte(ProfileIndication);
                stream.WriteByte(Compatibility);
                stream.WriteByte(LevelIndication);
                stream.WriteByte(0xff); // Reserved
                stream.WriteByte((byte)(0xE0 | SequenceParameterSets.Length));
                foreach (byte[] item in SequenceParameterSets) {
                    stream.WriteBEUInt16((UInt16)item.Length);
                    stream.WriteBytes(item);
                }
                stream.WriteByte((byte)PictureParameterSets.Length);
                foreach (byte[] item in PictureParameterSets) {
                    stream.WriteBEUInt16((UInt16)item.Length);
                    stream.WriteBytes(item);
                }
                if (ProfileHasExtraData(ProfileIndication)) {
                    stream.WriteByte((byte)(0xfc | ChromaFormatIdc));
                    stream.WriteByte((byte)(0xf8 | BitDepthLuma));
                    stream.WriteByte((byte)(0xf8 | BitDepthChroma));
                    stream.WriteByte((byte)SequenceParameterSetsExt.Length);
                    foreach (byte[] item in SequenceParameterSetsExt) {
                        stream.WriteBEUInt16((UInt16)item.Length);
                        stream.WriteBytes(item);
                    }
                }
            }

            public static bool ProfileHasExtraData(byte profile)
            {
                return (profile != 66) && (profile != 77) && (profile != 88);
            }
        }

        [MatrixIO.IO.Bmff.Box("pasp", "Aspect ratio")]
        public class AspectRatio : MatrixIO.IO.Bmff.Box
        {
            public AspectRatio()
                : base()
            {
                Numerator = 1;
                Denominator = 1;
            }

            public AspectRatio(System.IO.Stream stream) : base(stream) { }

            public UInt32 Numerator { get; set; }

            public UInt32 Denominator { get; set; }

            public override ulong CalculateSize()
            {
                return base.CalculateSize() + 4 + 4;
            }

            protected override void LoadFromStream(System.IO.Stream stream)
            {
                base.LoadFromStream(stream);
                Numerator = stream.ReadBEUInt32();
                Denominator = stream.ReadBEUInt32();
            }

            protected override void SaveToStream(System.IO.Stream stream)
            {
                base.SaveToStream(stream);
                stream.WriteBEUInt32(Numerator);
                stream.WriteBEUInt32(Denominator);
            }
        }

        [MatrixIO.IO.Bmff.Box("tfdt", "Track Fragment Base Media Decode Time")]
        public class Tfdt : MatrixIO.IO.Bmff.FullBox
        {
            public Tfdt()
                : base()
            {
                Version = 1;
            }

            public Tfdt(System.IO.Stream stream) : base(stream) { }

            public UInt64 BaseMediaDecodeTime { get; set; }

            public UInt64 TrackFragmentDuration { get; set; }

            public UInt32 NtpTimestampInteger { get; set; }

            public UInt32 NtpTimestampFraction { get; set; }

            public override ulong CalculateSize()
            {
                return base.CalculateSize() + (ulong)((Version == 1) ? 16 : 8) + (ulong)(HasNTP() ? 8 : 0);
            }

            protected override void LoadFromStream(System.IO.Stream stream)
            {
                base.LoadFromStream(stream);
                if (Version == 1) {
                    BaseMediaDecodeTime = stream.ReadBEUInt64();
                    TrackFragmentDuration = stream.ReadBEUInt64();
                } else /* Version == 0 */ {
                    BaseMediaDecodeTime = stream.ReadBEUInt32();
                    TrackFragmentDuration = stream.ReadBEUInt32();
                }
                if (HasNTP()) {
                    NtpTimestampInteger = stream.ReadBEUInt32();
                    NtpTimestampFraction = stream.ReadBEUInt32();
                }
            }

            protected override void SaveToStream(System.IO.Stream stream)
            {
                base.SaveToStream(stream);
                if (Version == 1) {
                    stream.WriteBEUInt64(BaseMediaDecodeTime);
                    stream.WriteBEUInt64(TrackFragmentDuration);
                } else /* Version == 0 */ {
                    stream.WriteBEUInt32((UInt32)BaseMediaDecodeTime);
                    stream.WriteBEUInt32((UInt32)TrackFragmentDuration);
                }
                if (HasNTP()) {
                    stream.WriteBEUInt32(NtpTimestampInteger);
                    stream.WriteBEUInt32(NtpTimestampFraction);
                }
            }

            private bool HasNTP()
            {
                return Flags.Get(1);
            }
        }

        private readonly uint SampleDuration = 512;

        private ulong streamCursor;
        private uint sequenceCounter;
        private uint totalCount;

        public BaseMedia CreateEmptyMP4(SpsParser videoData)
        {
            var test = new BaseMedia();
            {// File type
                var ftyp = new MatrixIO.IO.Bmff.Boxes.FileTypeBox();
                ftyp.CompatibleBrands.Add("isom");
                ftyp.CompatibleBrands.Add("iso2");
                ftyp.CompatibleBrands.Add("avc1");
                ftyp.CompatibleBrands.Add("iso6");
                ftyp.CompatibleBrands.Add("mp41");
                ftyp.MajorBrand = "isom";
                ftyp.MinorVersion = 512;
                test.Children.Add(ftyp);
            }
            {// Movie
                var moov = new MatrixIO.IO.Bmff.Boxes.MovieBox();
                {// Movie header
                    var mvhd = new MatrixIO.IO.Bmff.Boxes.MovieHeaderBox();
                    // Leave all default, except stuff from ffmpeg that isn't the same
                    mvhd.TimeScale = 1000;
                    mvhd.NextTrackID = 2;
                    moov.Children.Add(mvhd);
                }
                {// Track
                    var trak = new MatrixIO.IO.Bmff.Boxes.TrackBox();
                    {// Track header
                        var tkhd = new MatrixIO.IO.Bmff.Boxes.TrackHeaderBox();
                        tkhd.Width = MatrixIO.IO.Numerics.FixedPoint_16_16.FromDouble(videoData.Dimensions.Width);
                        tkhd.Height = MatrixIO.IO.Numerics.FixedPoint_16_16.FromDouble(videoData.Dimensions.Height);
                        tkhd.Enabled = true;
                        tkhd.InMovie = true;
                        tkhd.TrackID = 1;
                        trak.Children.Add(tkhd);
                    }
                    {// Media
                        var mdia = new MatrixIO.IO.Bmff.Boxes.MediaBox();
                        {// Media header
                            var mdhd = new MatrixIO.IO.Bmff.Boxes.MediaHeaderBox();
                            mdhd.TimeScale = 10240;
                            mdia.Children.Add(mdhd);
                        }
                        {// Handler
                            var hdlr = new MatrixIO.IO.Bmff.Boxes.HandlerBox();
                            hdlr.HandlerType = HDLR.TYPE_VIDEO;
                            hdlr.Name = HDLR.NAME_VIDEO;
                            mdia.Children.Add(hdlr);
                        }
                        {// Media information
                            var minf = new MatrixIO.IO.Bmff.Boxes.MediaInformationBox();
                            {// Video media header
                                var vmhd = new MatrixIO.IO.Bmff.Boxes.VideoMediaHeaderBox();
                                vmhd.NoLeanAhead = true;
                                minf.Children.Add(vmhd);
                            }
                            {// Data Information
                                var dinf = new MatrixIO.IO.Bmff.Boxes.DataInformationBox();
                                {// Data reference
                                    var dref = new MatrixIO.IO.Bmff.Boxes.DataReferenceBox();
                                    {// URL
                                        var url = new MatrixIO.IO.Bmff.Boxes.DataEntryUrlBox();
                                        url.MovieIsSelfContained = true;
                                        dref.Children.Add(url);
                                    }
                                    dinf.Children.Add(dref);
                                }
                                minf.Children.Add(dinf);
                            }
                            {// Sample table
                                var stbl = new MatrixIO.IO.Bmff.Boxes.SampleTableBox();
                                {// Sample description
                                    var stsd = new MatrixIO.IO.Bmff.Boxes.SampleDescriptionBox();
                                    {// Avc1
                                        var avc1 = new Avc1();
                                        avc1.VideoWidth = (UInt16)videoData.Dimensions.Width;
                                        avc1.VideoHeight = (UInt16)videoData.Dimensions.Height;
                                        {// avcC
                                            var avcc = new AvcC();
                                            avcc.ProfileIndication = videoData.Sps[1];
                                            avcc.Compatibility = videoData.Sps[2];
                                            avcc.LevelIndication = videoData.Sps[3];
                                            avcc.SequenceParameterSets = new byte[1][];
                                            avcc.SequenceParameterSets[0] = videoData.Sps;
                                            avcc.PictureParameterSets = new byte[1][];
                                            avcc.PictureParameterSets[0] = videoData.Pps;
                                            avc1.Children.Add(avcc);
                                        }
                                        {// Aspect ratio
                                            var pasp = new AspectRatio();
                                            avc1.Children.Add(pasp);
                                        }
                                        stsd.Children.Add(avc1);
                                    }
                                    stbl.Children.Add(stsd);
                                }
                                {// Time to Sample
                                    var stts = new MatrixIO.IO.Bmff.Boxes.TimeToSampleBox();
                                    stbl.Children.Add(stts);
                                }
                                {// Sample to Chunk
                                    var stsc = new MatrixIO.IO.Bmff.Boxes.SampleToChunkBox();
                                    stbl.Children.Add(stsc);
                                }
                                {// Sample size
                                    var stsz = new MatrixIO.IO.Bmff.Boxes.SampleSizeBox();
                                    stbl.Children.Add(stsz);
                                }
                                {// Chunk offset
                                    var stco = new MatrixIO.IO.Bmff.Boxes.ChunkOffsetBox();
                                    stbl.Children.Add(stco);
                                }
                                minf.Children.Add(stbl);
                            }
                            mdia.Children.Add(minf);
                        }
                        trak.Children.Add(mdia);
                    }
                    moov.Children.Add(trak);
                }
                {// Movie extends
                    var mvex = new MatrixIO.IO.Bmff.Boxes.MovieExtendsBox();
                    {// Track extends
                        var trex = new MatrixIO.IO.Bmff.Boxes.TrackExtendsBox();
                        trex.TrackID = 1;
                        trex.DefaultSampleDescriptionIndex = 1;
                        mvex.Children.Add(trex);
                    }
                    moov.Children.Add(mvex);
                }
                /*{// User data
                    var udta = new MatrixIO.IO.Bmff.Boxes.UserDataBox();
                    // TODO (e.g. ffmpeg mov_write_track_udta_tag writes title?)
                    moov.Children.Add(udta);
                }*/
                test.Children.Add(moov);
            }
            streamCursor = 0;
            foreach (var box in test.Children)
                streamCursor += box.CalculateSize();
            sequenceCounter = 0;
            totalCount = 0;
            return test;
        }

        public Box[] CreateChunk(byte[][] samples)
        {
            if (samples.Length == 0)
                return new Box[0];
            MatrixIO.IO.Bmff.Boxes.TrackRunBox trun;
            sequenceCounter++;
            // Movie fragment
            var moof = new MatrixIO.IO.Bmff.Boxes.MovieFragmentBox();
            {// Movie fragment Header
                var mfhd = new MatrixIO.IO.Bmff.Boxes.MovieFragmentHeaderBox();
                mfhd.SequenceNumber = sequenceCounter;
                moof.Children.Add(mfhd);
            }
            {// Track fragment
                var traf = new MatrixIO.IO.Bmff.Boxes.TrackFragmentBox();
                {// Track fragment header
                    var tfhd = new MatrixIO.IO.Bmff.Boxes.TrackFragmentHeaderBox();
                    tfhd.TrackID = 1;
                    tfhd.BaseDataOffset = streamCursor;
                    tfhd.DefaultSampleDuration = 1;
                    tfhd.DefaultSampleSize = (UInt32)samples[0].Length;
                    tfhd.DefaultSampleFlags = new MatrixIO.IO.Bmff.Boxes.SampleFlags { SampleDependsOn = 1, SampleIsDifferenceValue = true };
                    traf.Children.Add(tfhd);
                }
                {// Track fragment
                    var tfdt = new Tfdt();
                    tfdt.BaseMediaDecodeTime = totalCount * SampleDuration;
                    tfdt.TrackFragmentDuration = (ulong)samples.Length * SampleDuration;
                    traf.Children.Add(tfdt);
                }
                {// Track run
                    trun = new MatrixIO.IO.Bmff.Boxes.TrackRunBox();
                    trun.FirstSampleFlags = new MatrixIO.IO.Bmff.Boxes.SampleFlags { SampleDependsOn = 2 };
                    var entries = new List<MatrixIO.IO.Bmff.Boxes.TrackRunBox.TrackRunEntry>();
                    foreach (byte[] sample in samples) {
                        var entry = new MatrixIO.IO.Bmff.Boxes.TrackRunBox.TrackRunEntry();
                        entry.SampleSize = (UInt32)sample.Length;
                        entry.SampleDuration = SampleDuration;
                        entries.Add(entry);
                    }
                    trun.Entries = entries.ToArray();
                    traf.Children.Add(trun);
                }
                moof.Children.Add(traf);
            }
            // Fix up offsets
            trun.DataOffset = (int)moof.CalculateSize() + 12;
            // Movie data
            byte[] data = Flatten(Fix(samples));
            var mdat = new MatrixIO.IO.Bmff.Boxes.MovieDataBox(new System.IO.MemoryStream(data, false), 0, (ulong)data.Length);
            // Done
            var result = new Box[] { moof, mdat };
            foreach (var box in result)
                streamCursor += box.CalculateSize();
            totalCount += (uint)samples.Length;
            return result;
        }

        private static IEnumerable<byte[]> Fix(IEnumerable<byte[]> input)
        {
            foreach (var data in input)
                yield return Fix(data);
        }

        private static byte[] Fix(byte[] data)
        {
            uint[] parts = FindChunks(data);
            for (uint i = 1; i < parts.Length; i++) {
                uint tag = parts[i - 1];
                uint endTag = parts[i];
                byte[] buffer = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer, (endTag - tag) - 4);
                Buffer.BlockCopy(buffer, 0, data, (int)tag, buffer.Length);
            }
            return data;
        }

        private static uint[] FindChunks(byte[] data)
        {
            List<uint> results = new List<uint>();
            int found = 0;
            while ((found = SpsParser.FindPattern(data, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, found)) != -1) {
                results.Add((uint)found);
                found += RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length;
            }
            if (results.Count != 0)
                results.Add((uint)data.Length);
            return results.ToArray();
        }

        private static byte[] Flatten(IEnumerable<byte[]> data)
        {
            int total = 0;
            foreach (byte[] sample in data)
                total += sample.Length;
            byte[] result = new byte[total];
            int i = 0;
            foreach (byte[] sample in data) {
                Buffer.BlockCopy(sample, 0, result, i, sample.Length);
                i += sample.Length;
            }
            return result;
        }
    }
}
