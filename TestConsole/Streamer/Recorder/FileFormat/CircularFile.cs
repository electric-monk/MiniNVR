using System;
using System.IO.MemoryMappedFiles;

namespace TestConsole.Streamer.Recorder.FileFormat
{
    public class CircularFile<HeaderType> where HeaderType : Header, new()
    {
        protected MemoryMappedFile dataFile;
        private UInt64 dataSize;
        private HeaderType oldest, current;

        public HeaderType Oldest
        {
            get {
                return oldest;
            }
        }

        public HeaderType Current
        {
            get {
                return current;
            }
        }

        public CircularFile(string filename, UInt64 maxSize)
        {
            dataSize = maxSize;
            dataFile = MemoryMappedFile.CreateFromFile(filename, System.IO.FileMode.OpenOrCreate, null, (long)dataSize, MemoryMappedFileAccess.ReadWrite);
            HeaderType active = new HeaderType();
            active.Offset = 0;
            if (!active.Load(dataFile)) {
                // Format the file
                active.NextHeader = -1;
                active.PreviousHeader = -1;
                active.FreeSpaceFollowing = dataSize - active.Length;
                active.Save(dataFile);
                oldest = active;
                current = active;
            } else {
                // Find oldest
                oldest = active;
                while (oldest.PreviousHeader != -1) {
                    HeaderType prev = new HeaderType();
                    prev.Offset = (UInt64)oldest.PreviousHeader;
                    if (!prev.Load(dataFile))
                        break;
                    oldest = prev;
                }
                // Find newest
                current = active;
                while (current.NextHeader != -1) {
                    HeaderType next = new HeaderType();
                    next.Offset = (UInt64)current.NextHeader;
                    if (!next.Load(dataFile))
                        break;
                    current = next;
                }
            }
        }

        public void Stop()
        {
            dataFile.Dispose();
        }

        private void Expand(ref UInt64 available, UInt64 required)
        {
            while (available < required) {
                available += oldest.Length + oldest.DataSize + oldest.FreeSpaceFollowing;
                HeaderType next = new HeaderType();
                next.Offset = (UInt64)oldest.NextHeader;
                if (!next.Load(dataFile))
                    throw new CorruptedException("Failed to find expected data, unable to recover");
                oldest = next;
            }
            oldest.PreviousHeader = -1;
        }

        private bool Expand(HeaderType header, UInt64 required)
        {
            if ((header.Offset + header.Length + header.DataSize + required) > dataSize)
                return false;
            UInt64 available = header.FreeSpaceFollowing;
            Expand(ref available, required);
            header.FreeSpaceFollowing = available;
            return true;
        }

        public HeaderType GetHeader(Int64 offset)
        {
            if (offset == -1)
                return null;
            HeaderType result = new HeaderType() { Offset = (UInt64)offset };
            if (!result.Load(dataFile))
                result = null;
            return result;
        }

        public void SaveHeader(HeaderType newHeader)
        {
            GetSpaceForHeader(newHeader).Dispose();
        }

        public DataWrapper GetSpaceForHeader(HeaderType newHeader)
        {
            newHeader.CheckSize();
            UInt64 required = newHeader.Length + newHeader.DataSize;
            if (Expand(current, required)) {
                newHeader.Offset = current.Offset + current.Length + current.DataSize;
                newHeader.FreeSpaceFollowing = current.FreeSpaceFollowing - required;
            } else {
                oldest = new HeaderType();
                oldest.Offset = 0;
                oldest.Load(dataFile);
                newHeader.Offset = 0;
                UInt64 available = 0;
                Expand(ref available, required);
                newHeader.FreeSpaceFollowing = available - required;
            }
            newHeader.NextHeader = -1;
            newHeader.PreviousHeader = (Int64)current.Offset;
            return new DataWrapperImpl(this, newHeader);
        }

        private void CommitNewData(HeaderType obj)
        {
            // First, update oldest, thus reserving the space
            oldest.Update(dataFile);
            // Second, save the new data, so it's there
            obj.Save(dataFile);
            // Finally, link the new data, to complete the update
            if (obj.Offset > current.Offset)
                current.FreeSpaceFollowing = 0;
            else
                current.FreeSpaceFollowing = dataSize - (current.Offset + current.Length + current.DataSize);
            current.NextHeader = (Int64)obj.Offset;
            current.Update(dataFile);
            current = obj;
            // Hopefully, doing things in the above order means that a power failure will not corrupt the structure
        }

        public abstract class DataWrapper : IDisposable
        {
            public DataWrapper(HeaderType data)
            {
                Data = data;
            }

            public HeaderType Data { get; }

            public abstract void Dispose();
        }

        private class DataWrapperImpl : DataWrapper
        {
            private CircularFile<HeaderType> owner;
            private bool disposedValue;

            public DataWrapperImpl(CircularFile<HeaderType> container, HeaderType header)
                :base(header)
            {
                owner = container;
            }

            public override void Dispose()
            {
                if (!disposedValue) {
                    owner.CommitNewData(Data);
                    disposedValue = true;
                }
                GC.SuppressFinalize(this);
            }
        }

        public class CorruptedException : Exception
        {
            public CorruptedException(string s)
                :base(s)
            {
            }
        }
    }
}
