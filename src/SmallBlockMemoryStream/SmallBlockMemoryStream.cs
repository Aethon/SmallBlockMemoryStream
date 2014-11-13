using System;
using System.Collections.Generic;
using System.IO;

namespace Aethon.IO
{
    public class SmallBlockMemoryStream : Stream
    {
        public const int StartBlockCount = 10;
        public const int MinBlockSize = 256;
        public const int MaxBlockSize = 85000 - 32; // should fly under the LOH

        private static readonly int[] NoAllocations = new int[0];

        private byte[][] _blocks;
        private int _blockCount;

        private long _length;
        private long _capacity;

        private int _cursorIndex;
        private long _cursorBase;
        private int _cursorOffset;

        private bool _closed;

        public override bool CanRead
        {
            get { return !_closed; }
        }

        public override bool CanSeek
        {
            get { return !_closed; }
        }

        public override bool CanWrite
        {
            get { return !_closed; }
        }

        public override void Flush()
        {
            // to remain consistent with MemoryStream, this does not check
            //  the open/closed/disposed state of the stream
        }

        public override long Length
        {
            get
            {
                if (_closed)
                    throw __Error.StreamIsClosed();

                return _length;
            }
        }

        public override long Position
        {
            get
            {
                if (_closed)
                    throw __Error.StreamIsClosed();
                
                return _cursorBase + _cursorOffset;
            }
            set
            {
                if (_closed)
                    throw __Error.StreamIsClosed();
                if (value < 0)
                    throw __Error.NeedNonNegNumber(null);
                
                SetPosition(value);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_closed)
                throw __Error.StreamIsClosed();
            if (buffer == null)
                throw __Error.NullArgument("buffer");
            if (offset < 0)
                throw __Error.NeedNonNegNumber("offset");
            if (count < 0)
                throw __Error.NeedNonNegNumber("count");
            if (buffer.Length - offset < count)
                throw __Error.InvalidOffset("offset");

            if (count == 0) return 0;

            var cursorIndex = _cursorIndex;
            var cursorOffset = _cursorOffset;
            var cursorBase = _cursorBase;

            var read = 0;
            var toRead = _length - cursorBase - cursorOffset;
            if (count < toRead)
                toRead = count;
            while (toRead > 0)
            {
                var block = _blocks[cursorIndex];
                var blockLength = block.Length;
                var available = blockLength - cursorOffset;
                var readCount = (int)(available < toRead ? available : toRead);
                Buffer.BlockCopy(block, cursorOffset, buffer, offset + read, readCount);
                toRead -= readCount;
                read += readCount;
                cursorOffset += readCount;
                if (cursorOffset != blockLength) break;
                cursorIndex++;
                cursorBase += blockLength;
                cursorOffset = 0;
            }

            _cursorIndex = cursorIndex;
            _cursorOffset = cursorOffset;
            _cursorBase = cursorBase;

            return read;
        }

        public override int ReadByte()
        {
            if (_closed)
                throw __Error.StreamIsClosed();

            var cursorOffset = _cursorOffset;
            var cursorBase = _cursorBase;
            if (cursorBase + cursorOffset >= _length)
                return -1;

            var cursorIndex = _cursorIndex;
            var block = _blocks[cursorIndex];
            var result = block[cursorOffset++];
            
            var blockLength = block.Length;
            if (cursorOffset == blockLength)
            {
                _cursorIndex = cursorIndex + 1;
                _cursorBase = cursorBase + blockLength;
                cursorOffset = 0;
            }
            _cursorOffset = cursorOffset;
            
            return result;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_closed)
                throw __Error.StreamIsClosed();

            long newPos;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPos = offset;
                    if (newPos < 0)
                        throw __Error.SeekBeforeBegin();
                    break;
                case SeekOrigin.Current:
                    newPos = _cursorBase + _cursorOffset + offset;
                    if (newPos < 0)
                        throw __Error.SeekBeforeBegin();
                    break;
                case SeekOrigin.End:
                    newPos = _length + offset;
                    if (newPos < 0)
                        throw __Error.SeekBeforeBegin();
                    break;
                default:
                    throw __Error.UnknownSeekOrigin(origin, "origin");
            }

            return SetPosition(newPos);
        }

        public override void SetLength(long value)
        {
            if (_closed)
                throw __Error.StreamIsClosed();
            if (value < 0)
                throw __Error.NeedNonNegNumber("value");

            EnsureCapacity(value);
            if (value < _length)
            {
                // zero out the area we are "discarding"
                var index = 0;
                var start = value;
                var count = _length - start;
                do
                {
                    var size = _blocks[index].Length;
                    if (start < size) break;
                    start -= size;
                    index++;
                } while (true);
                do
                {
                    var block = _blocks[index];
                    var available = (int)(block.Length - start);
                    var toClear = (int)(available < count ? available : count);
                    Array.Clear(block, (int)start, toClear);
                    count -= toClear;
                    start = 0;
                    index++;
                } while (count > 0);

                if (value < _cursorBase + _cursorOffset)
                    SetPosition(value);
            }
            _length = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_closed)
                throw __Error.StreamIsClosed();
            if (buffer == null)
                throw __Error.NullArgument("buffer");
            if (offset < 0)
                throw __Error.NeedNonNegNumber("offset");
            if (count < 0)
                throw __Error.NeedNonNegNumber("count");
            if (buffer.Length - offset < count)
                throw __Error.InvalidOffset("offset");

            if (count == 0) return;

            var cursorOffset = _cursorOffset;
            var cursorBase = _cursorBase;
            EnsureCapacity(cursorBase + cursorOffset + count);

            var cursorIndex = _cursorIndex;
            do
            {
                var block = _blocks[cursorIndex];
                var blockLength = block.Length;
                var writeAvailable = blockLength - cursorOffset;
                var writeCount = writeAvailable < count ? writeAvailable : count;
                Buffer.BlockCopy(buffer, offset, block, cursorOffset, writeCount);
                count -= writeCount;
                offset += writeCount;
                cursorOffset += writeCount;
                if (cursorOffset != blockLength) break;
                cursorIndex++;
                cursorBase += blockLength;
                cursorOffset = 0;
            } while (count > 0);

            _cursorIndex = cursorIndex;
            _cursorOffset = cursorOffset;
            _cursorBase = cursorBase;

            var position = cursorBase + cursorOffset;
            if (position > _length)
                _length = position;
        }

        public override void WriteByte(byte value)
        {
            if (_closed)
                throw __Error.StreamIsClosed();

            var cursorOffset = _cursorOffset;
            var cursorBase = _cursorBase;
            EnsureCapacity(cursorBase + cursorOffset + 1);

            var cursorIndex = _cursorIndex;

            var block = _blocks[cursorIndex];
            block[cursorOffset++] = value;

            var size = block.Length;
            if (cursorOffset == size)
            {
                cursorIndex++;
                cursorBase += size;
                cursorOffset = 0;
            }

            _cursorIndex = cursorIndex;
            _cursorOffset = cursorOffset;
            _cursorBase = cursorBase;

            var position = cursorBase + cursorOffset;
            if (position > _length)
                _length = position;
        }

        public int[] GetAllocationSizes()
        {
            if (_blocks == null)
                return NoAllocations;

            var result = new List<int>();
            foreach (var block in _blocks)
                result.Add(block == null ? -1 : block.Length);

            return result.ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closed = true;
                _blocks = null;
            }
            base.Dispose(disposing);
        }

        private long SetPosition(long position)
        {
            if (position > _length)
            {
                EnsureCapacity(position);
                _length = position;
            }

            var cursorBase = 0;
            var cursorIndex = 0;
            var cursorOffset = position;
            while (cursorIndex < _blockCount)
            {
                var blockLength = _blocks[cursorIndex].Length;
                if (cursorOffset < blockLength) break;
                cursorBase += blockLength;
                cursorIndex++;
                cursorOffset -= blockLength;
            }
            _cursorIndex = cursorIndex;
            _cursorBase = cursorBase;
            _cursorOffset = (int)cursorOffset;

            return position;
        }

        private void EnsureCapacity(long newCapacity)
        {
            var capacity = _capacity;
            if (capacity >= newCapacity)
                return;

            var blockCount = _blockCount;
            var blocks = _blocks;

            // determine required allocations based on the MemoryStream algorithm
            var extraRequired = newCapacity - capacity;
            var toAllocate = extraRequired > MinBlockSize ? extraRequired : MinBlockSize;
            if (capacity > toAllocate)
                toAllocate = capacity; // this effects the doubling-algorithm from MemoryStream
            int newBlocks;
            int newBlockSize;
            if (toAllocate <= MaxBlockSize)
            {
                newBlockSize = (int)toAllocate;
                newBlocks = 1;
            }
            else
            {
                newBlockSize = MaxBlockSize;
                newBlocks = (int)(toAllocate / MaxBlockSize);
                var mod = toAllocate % MaxBlockSize;
                if (mod != 0)
                    newBlocks++;
            }

            // extend the block array as necessary
            if (blocks == null)
            {
                var count = newBlocks > StartBlockCount ? newBlocks : StartBlockCount;
                blocks = new byte[count][];
                _blocks = blocks;
            }
            else if (blockCount + newBlocks > _blocks.Length)
            {
                var nextblocks = new byte[_blocks.Length * 2][];
                Array.Copy(blocks, nextblocks, blocks.Length);
                blocks = nextblocks;
                _blocks = blocks;
            }

            // create the actual blocks
            for (var i = 0; i < newBlocks; i++)
                blocks[blockCount++] = new byte[newBlockSize];

            _capacity += toAllocate;
            _blockCount = blockCount;
        }
    }
}
