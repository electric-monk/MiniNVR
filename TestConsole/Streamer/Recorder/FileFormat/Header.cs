using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace TestConsole.Streamer.Recorder.FileFormat
{
    /** Base header class defining the fields required for the circular buffer, but not any contents. */
    public abstract class Header
    {
        private UInt32 dataSize;

        public bool Load(MemoryMappedFile dataFile)
        {
            Int32 headerLength = (Int32)Length; 
            byte[] data = new byte[headerLength];
            using (var view = dataFile.CreateViewStream((long)Offset, headerLength)) {
                if (view.Read(data, 0, (int)Length) != Length)
                    return false;
            }
            UInt32 expectedChecksum = ComputeChecksum(new ArraySegment<byte>(data, 4, data.Length - 4));
            using (var reader = new BinaryReader(new MemoryStream(data))) {
                UInt32 gotChecksum = reader.ReadUInt32();
                if (expectedChecksum != gotChecksum)
                    return false;
                PreviousHeader = reader.ReadInt64();
                NextHeader = reader.ReadInt64();
                FreeSpaceFollowing = reader.ReadUInt64();
                dataSize = reader.ReadUInt32();
                LoadExtraHeader(reader);
            }
            return true;
        }

        public void LoadData(MemoryMappedFile dataFile)
        {
            using (var view = dataFile.CreateViewStream((long)Offset + Length, DataSize)) {
                using (var reader = new BinaryReader(view)) {
                    LoadData(reader);
                }
            }
        }

        private byte[] ComputeHeader()
        {
            byte[] data = new byte[Length];
            using (var writer = new BinaryWriter(new MemoryStream(data))) {
                writer.Write((UInt32)0);  // Checksum space
                writer.Write(PreviousHeader);
                writer.Write(NextHeader);
                writer.Write(FreeSpaceFollowing);
                writer.Write(dataSize);
                SaveExtraHeader(writer);
            }
            using (var writer = new BinaryWriter(new MemoryStream(data))) {
                writer.Write((UInt32)ComputeChecksum(new ArraySegment<byte>(data, 4, data.Length - 4)));
            }
            return data;
        }

        public void CheckSize()
        {
            dataSize = ComputeDataSize();
        }

        public void Save(MemoryMappedFile dataFile)
        {
            CheckSize();
            byte[] data = ComputeHeader();
            using (var view = dataFile.CreateViewStream((long)Offset, Length + DataSize)) {
                using (var writer = new BinaryWriter(view)) {
                    writer.Write(data);
                    SaveData(writer);
                }
            }
        }

        public void Update(MemoryMappedFile dataFile)
        {
            byte[] data = ComputeHeader();
            using (var view = dataFile.CreateViewStream((long)Offset, Length + DataSize)) {
                using (var writer = new BinaryWriter(view)) {
                    writer.Write(data, 0, 28);
                }
            }
        }

        protected abstract UInt32 ComputeDataSize();

        protected abstract void LoadExtraHeader(BinaryReader reader);

        protected abstract void SaveExtraHeader(BinaryWriter writer);

        protected abstract void LoadData(BinaryReader reader);

        protected abstract void SaveData(BinaryWriter writer);

        public UInt64 Offset { get; set; }

        public virtual UInt32 Length {
            get {
                return 32;
            }
        }

        /** Index to previous header in linked list */
        public Int64 PreviousHeader { get; set; }

        /** Index to next header in linked list */
        public Int64 NextHeader { get; set; }

        /** Amount of free space after header */
        public UInt64 FreeSpaceFollowing { get; set; }

        /** Total size of data */
        public UInt32 DataSize {
            get {
                return dataSize;
            }
        }

        private static UInt32 ComputeChecksum(IEnumerable<byte> data)
        {
            UInt32 result = 0xAA55AA55;
            foreach (byte b in data) {
                result ^= b;
                result = ((result & 0x80000000) >> 7) | ((result & 0x7FFFFFFF) << 1);
            }
            return result;
        }
    }
}
