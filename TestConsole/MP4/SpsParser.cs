using System;
using System.Collections.Generic;
using System.Linq;

namespace TestConsole.MP4
{
    public class SpsParser : Mp4Metadata
    {
        private readonly byte[] sps;
        private readonly byte[] pps;

        public SpsParser(byte[] spspps)
        {
            byte[][] parts = Split(spspps);
            sps = (parts.Length >= 1) ? parts[0] : new byte[0];
            pps = (parts.Length >= 2) ? parts[1] : new byte[0];
        }

        public override byte[] Sps { get { return sps; } }

        public override byte[] Pps { get { return pps; } }

        public static byte[][] Split(byte[] input)
        {
            List<byte[]> result = new List<byte[]>();
            int last = 0;
            while (last < input.Length) {
                int next = FindPattern(input, RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker, last);
                if (next == -1)
                    break;
                if (last != 0)
                    result.Add(input.Skip(last).Take(next - last).ToArray());
                last = next + RtspClientSharp.RawFrames.Video.RawH264Frame.StartMarker.Length;
            }
            result.Add(input.Skip(last).Take(input.Length - last).ToArray());
            return result.ToArray();
        }

        public static int FindPattern(byte[] input, byte[] pattern, int from)
        {
            int count = input.Length;
            int match = pattern.Length;
            count -= match;
            for (int i = from; i < count; i++) {
                int j;
                for (j = 0; j < match; j++)
                    if (pattern[j] != input[i + j])
                        break;
                if (j == match)
                    return i;
            }
            return -1;
        }

    }
}
