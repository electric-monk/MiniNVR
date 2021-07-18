using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestConsole.MP4
{
    public abstract class Mp4Metadata
    {
        public struct FrameSize
        {
            public int Width, Height;
        }

        private bool dimensionsComputed = false;
        private FrameSize dimensions;

        public abstract byte[] Sps { get; }

        public abstract byte[] Pps { get; }

        public FrameSize Dimensions
        {
            get {
                if (!dimensionsComputed) {
                    dimensionsComputed = true;
                    ParseSps();
                }
                return dimensions;
            }
        }

        private void ParseSps()
        {
            try {
                // Based on https://github.com/angrycoding/sps-parser/blob/master/parse_sps.c
                Reader reader = new Reader(Sps);
                reader.ReadBits(8);
                byte profileIdc = (byte)reader.ReadBits(8);
                reader.ReadBits(16);
                reader.ReadUEG();
                if (Array.Exists(Reader.profiles, e => e == profileIdc)) {
                    UInt32 chromaFormatIdc = reader.ReadUEG();
                    if (chromaFormatIdc == 3)
                        reader.ReadBits(1);
                    reader.ReadUEG();
                    reader.ReadUEG();
                    reader.ReadBits(1);
                    if (reader.ReadBits(1) != 0) {
                        int count = chromaFormatIdc != 3 ? 8 : 12;
                        for (int i = 0; i < count; i++)
                            if (reader.ReadBits(1) != 0)
                                reader.SkipScalingList(i < 6 ? 16 : 64);
                    }
                }
                reader.ReadUEG();
                UInt32 pict_order_cnt_type = reader.ReadUEG();
                if (pict_order_cnt_type == 0) {
                    reader.ReadUEG();
                } else if (pict_order_cnt_type == 1) {
                    reader.ReadBits(1);
                    reader.ReadEG();
                    reader.ReadEG();
                    UInt32 count = reader.ReadUEG();    // CHECK THIS
                    for (UInt32 i = 0; i < count; i++)
                        reader.ReadEG();
                }
                reader.ReadUEG();
                reader.ReadBits(1);
                UInt32 picWidthInMbsMinus1 = reader.ReadUEG();
                UInt32 picHeightInMapUnitsMinus1 = reader.ReadUEG();
                UInt32 frameMbsOnlyFlag = reader.ReadBits(1);
                if (frameMbsOnlyFlag == 0)
                    reader.ReadBits(1);
                reader.ReadBits(1);
                UInt32 frameCropLeftOffset = 0;
                UInt32 frameCropRightOffset = 0;
                UInt32 frameCropTopOffset = 0;
                UInt32 frameCropBottomOffset = 0;
                if (frameMbsOnlyFlag != 0) {
                    frameCropLeftOffset = reader.ReadUEG();
                    frameCropRightOffset = reader.ReadUEG();
                    frameCropTopOffset = reader.ReadUEG();
                    frameCropBottomOffset = reader.ReadUEG();
                }

                dimensions.Width = (int)(((picWidthInMbsMinus1 + 1) * 16) - (frameCropLeftOffset * 2) - (frameCropRightOffset * 2));
                dimensions.Height = (int)(((2 - frameMbsOnlyFlag) * ((picHeightInMapUnitsMinus1 + 1) * 16)) - (((frameMbsOnlyFlag != 0) ? 2 : 4) * (frameCropTopOffset + frameCropBottomOffset)));
            }
            catch {
                dimensions.Width = 0;
                dimensions.Height = 0;
            }
        }

        private class Reader
        {
            public static readonly byte[] profiles = new byte[] { 100, 110, 122, 244, 44, 83, 86, 118, 128 };

            private readonly byte[] buffer;
            private uint offset;

            public Reader(byte[] data)
            {
                buffer = data;
                offset = 0;
            }

            public UInt32 ReadBits(uint count)
            {
                UInt32 result = 0;
                uint index = offset / 8;
                uint bitNumber = offset - (index * 8);
                uint outBitNumber = count - 1;
                for (int c = 0; c < count; c++) {
                    if (((buffer[index] << (byte)bitNumber) & 0x80) != 0)
                        result |= (UInt32)(1 << (int)outBitNumber);
                    if (++bitNumber > 7) {
                        bitNumber = 0;
                        index++;
                    }
                    outBitNumber--;
                }
                offset += count;
                return result;
            }

            public UInt32 ReadUEG()
            {
                byte bitcount = 0;
                while (ReadBits(1) == 0)
                    bitcount++;
                UInt32 result = 0;
                if (bitcount != 0) {
                    UInt32 value = ReadBits(bitcount);
                    result = (UInt32)(1 << bitcount) - 1 + value;
                }
                return result;
            }

            public Int32 ReadEG()
            {
                UInt32 value = ReadUEG();
                if ((value & 0x01) != 0)
                    return (Int32)((value + 1) / 2);
                else
                    return -(Int32)(value / 2);
            }

            public void SkipScalingList(int skip)
            {
                UInt32 lastScale = 8;
                UInt32 nextScale = 8;
                for (int j = 0; j < skip; j++) {
                    if (nextScale != 0) {
                        Int32 deltaScale = ReadEG(); // CHECK THIS
                        nextScale = (UInt32)((lastScale + deltaScale + 256) % 256);
                    }
                    lastScale = (nextScale == 0) ? lastScale : nextScale;
                }
            }
        }
    }
}
