/*
 *  Licensed to the Apache Software Foundation (ASF) under one or more
 *  contributor license agreements.  See the NOTICE file distributed with
 *  this work for additional information regarding copyright ownership.
 *  The ASF licenses this file to You under the Apache License, Version 2.0
 *  (the "License"); you may not use this file except in compliance with
 *  the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 *  Unless required by applicable law or agreed to in writing, software
 *  distributed under the License is distributed on an "AS IS" BASIS,
 *  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *  See the License for the specific language governing permissions and
 *  limitations under the License.
 *
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

#if YGZIPLIB && DEFLATE64

namespace YGZipLib.Streams
{

    /// <summary>
    /// BitInputStream
    /// Apache Commons Compress Release 1.21から移植
    /// src/main/java/org/apache/commons/compress/compressors/deflate64
    ///   Deflate64CompressorInputStream.java
    ///   HuffmanDecoder.java
    ///   HuffmanState.java
    /// </summary>
    class Deflate64Stream : Stream
    {

        #region enum/const
        
        enum HuffmanState
        {
            INITIAL,
            STORED,
            DYNAMIC_CODES,
            FIXED_CODES
        }

        private const int BYTE_SIZE = 8;

        #endregion

        #region メンバ変数

        private readonly bool leaveOpen;
        private Stream originalStream;
        private HuffmanDecoder decoder;

        #endregion

        #region "constructor"

        public Deflate64Stream(Stream stream, CompressionMode mode)
            : this(stream, mode, false)
        { }

        public Deflate64Stream(Stream stream, CompressionMode mode, bool leaveOpen)
        {
            if(mode == CompressionMode.Compress)
            {
                throw new NotSupportedException();
            }
            this.originalStream = stream;
            this.decoder = new HuffmanDecoder(stream);
            this.leaveOpen = leaveOpen;
        }

        #endregion

        #region private class

        private class HuffmanDecoder : IDisposable
        {

            #region static

            private static readonly Int16[] RUN_LENGTH_TABLE = {
                96, 128, 160, 192, 224, 256, 288, 320, 353, 417, 481, 545, 610, 738, 866,
                994, 1123, 1379, 1635, 1891, 2148, 2660, 3172, 3684, 4197, 5221, 6245, 7269, 112
            };

            private static readonly Int32[] DISTANCE_TABLE = {
                16, 32, 48, 64, 81, 113, 146, 210, 275, 403,  // 0-9
                532, 788, 1045, 1557, 2070, 3094, 4119, 6167, 8216, 12312, // 10-19
                16409, 24601, 32794, 49178, 65563, 98331, 131100, 196636, 262173, 393245, // 20-29
                524318, 786462 // 30-31
            };

            private static readonly Int32[] CODE_LENGTHS_ORDER = {
                16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
            };

            private static readonly int[] FIXED_LITERALS;
            private static readonly int[] FIXED_DISTANCE;

            static HuffmanDecoder()
            {
                FIXED_LITERALS = new Int32[288];
                ArraysFill(FIXED_LITERALS, 0, 144, 8);
                ArraysFill(FIXED_LITERALS, 144, 256, 9);
                ArraysFill(FIXED_LITERALS, 256, 280, 7);
                ArraysFill(FIXED_LITERALS, 280, 288, 8);

                FIXED_DISTANCE = new Int32[32];
                ArraysFill(FIXED_DISTANCE, 0, 32, 5);

            }

            static void ArraysFill(int[] targetArray, int startIndex, int endIndex, int value)
            {
                unsafe
                {
                    fixed (int* tp = targetArray)
                    {
                        int* tip = tp + startIndex;
                        for (int i = startIndex; i < endIndex; i++)
                        {
                            *tip++ = value;
                        }
                    }
                }
            }

            #endregion

            #region メンバ変数

            private bool finalBlock;
            private DecoderState state;
            private BitInputStream reader;
            private DecodingMemory memory = new DecodingMemory();

            #endregion

            #region Private Class(DecoderState)

            internal abstract class DecoderState
            {
                private protected BitInputStream reader;

                private protected DecodingMemory memory;

                internal protected DecoderState(BitInputStream reader, DecodingMemory memory)
                {
                    this.reader = reader;
                    this.memory = memory;
                }

                internal abstract HuffmanState State();

                internal abstract int Read(byte[] b, int off, int len);

                internal abstract bool HasData();

                internal abstract int Available();

            }

            private class InitialState : DecoderState
            {
                internal InitialState(BitInputStream reader)
                    : base(reader, null)
                {
                }

                internal override int Available()
                {
                    return 0;
                }

                internal override bool HasData()
                {
                    return false;
                }

                internal override int Read(byte[] b, int off, int len)
                {
                    if (len == 0)
                    {
                        return 0;
                    }
                    throw new System.IO.InvalidDataException("Cannot read in this state");
                }

                internal override HuffmanState State()
                {
                    return HuffmanState.INITIAL;
                }
            }

            private class UncompressedState : DecoderState
            {
                private readonly long blockLength = 0;
                private long read = 0;

                internal UncompressedState(long blockLength, BitInputStream reader, DecodingMemory memory)
                    : base(reader, memory)
                {
                    this.blockLength = blockLength;
                }

                internal override int Available()
                {
                    return (int)Math.Min(blockLength - read, reader.BitsAvailable() / BYTE_SIZE);
                }

                internal override bool HasData()
                {
                    return read < blockLength;
                }

                internal override int Read(byte[] b, int off, int len)
                {
                    if (len == 0)
                    {
                        return 0;
                    }
                    // as len is an int and (blockLength - read) is >= 0 the min must fit into an int as well
                    int max = (int)Math.Min(blockLength - read, len);
                    int readSoFar = 0;
                    while (readSoFar < max)
                    {
                        int readNow;
                        if (reader.BitsCached > 0)
                        {

                            // バイト境界ではない場合、8bit単位の読込となる。
                            // BitsCached は AlignWithByteBoundary() をCallしているので 0 のはず
                            // Decodeの処理を見る限りこの処理不要では？
                            throw new InvalidDataException("Truncated Deflate64 Stream");

                            //byte next = (byte)ReadBits(reader,BYTE_SIZE);
                            //b[off + readSoFar] = memory.Add(next);
                            //readNow = 1;
                        }
                        else
                        {
                            readNow = reader.Read(b, off + readSoFar, max - readSoFar);
                            if (readNow <= 0)
                            {
                                throw new InvalidDataException("Truncated Deflate64 Stream");
                            }

                            memory.Add(b, off + readSoFar, readNow);

                        }
                        read += readNow;
                        readSoFar += readNow;
                    }
                    return max;

                }

                internal override HuffmanState State()
                {
                return read < blockLength ? HuffmanState.STORED : HuffmanState.INITIAL;
                }
            }

            private class HuffmanCodes : DecoderState
            {

                private bool endOfBlock;
                private readonly HuffmanState state;
                private readonly BinaryTreeNode lengthTree;
                private readonly BinaryTreeNode distanceTree;

                private int runBufferPos;
                private byte[] runBuffer = Array.Empty<byte>();
                private int runBufferLength;

                public HuffmanCodes(HuffmanState state, int[] lengths, int[] distance,BitInputStream reader, DecodingMemory memory)
                    : base(reader,memory)
                {
                    this.state = state;
                    lengthTree = BinaryTreeNode.BuildTree(lengths);
                    distanceTree = BinaryTreeNode.BuildTree(distance);
                }

                internal override int Available()
                {
                    return runBufferLength - runBufferPos;
                }

                internal override bool HasData()
                {
                    return !endOfBlock;
                }

                internal override int Read(byte[] b, int off, int len)
                {
                    if (len == 0)
                    {
                        return 0;
                    }
                    return DecodeNext(b, off, len);
                }

                internal override HuffmanState State()
                {
                    return endOfBlock ? HuffmanState.INITIAL : state;
                }

                private int DecodeNext(byte[] b, int off, int len)
                {
                    if (endOfBlock)
                    {
                        return -1;
                    }
                    int result = CopyFromRunBuffer(b, off, len);

                    while (result < len)
                    {
                        int symbol = NextSymbol(reader, lengthTree);
                        if (symbol < 256)
                        {
                            b[off + result++] = memory.Add((byte)symbol);
                        }
                        else if (symbol > 256)
                        {
                            int runMask = RUN_LENGTH_TABLE[symbol - 257];
                            int run = runMask >> 5;
                            int runXtra = runMask & 0x1F;
                            run += (int)ReadBits(reader, runXtra);

                            int distSym = NextSymbol(reader, distanceTree);

                            int distMask = DISTANCE_TABLE[distSym];
                            int dist = distMask >> 4;
                            int distXtra = distMask & 0xF;
                            dist += (int)ReadBits(reader,distXtra);

                            if (runBuffer.Length < run)
                            {
                                runBuffer = new byte[run];
                            }
                            runBufferLength = run;
                            runBufferPos = 0;
                            memory.RecordToBuffer(dist, run, runBuffer);

                            result += CopyFromRunBuffer(b, off + result, len - result);
                        }
                        else
                        {
                            endOfBlock = true;
                            return result;
                        }
                    }

                    return result;
                }

                private int CopyFromRunBuffer(byte[] b, int off, int len)
                {
                    int bytesInBuffer = runBufferLength - runBufferPos;
                    int copiedBytes = 0;
                    if (bytesInBuffer > 0)
                    {
                        copiedBytes = Math.Min(len, bytesInBuffer);
                        Array.Copy(runBuffer, runBufferPos, b, off, copiedBytes);
                        runBufferPos += copiedBytes;
                    }
                    return copiedBytes;
                }

                private static int NextSymbol(BitInputStream reader,  BinaryTreeNode tree) 
                {
                    BinaryTreeNode node = tree;
                    while (node != null && node.literal == -1)
                    {
                        long bit = ReadBits(reader, 1);
                        node = bit == 0 ? node.leftNode : node.rightNode;
                    }
                    return node != null ? node.literal : -1;
                }


                internal static void PopulateDynamicTables(BitInputStream reader, int[] literals, int[] distances)
                {
                    int codeLengths = (int)(ReadBits(reader, 4) + 4);

                    int[] codeLengthValues = new int[19];
                    for (int cLen = 0; cLen < codeLengths; cLen++)
                    {
                        codeLengthValues[CODE_LENGTHS_ORDER[cLen]] = (int)ReadBits(reader, 3);
                    }

                    BinaryTreeNode codeLengthTree = BinaryTreeNode.BuildTree(codeLengthValues);

                    int[] auxBuffer = new int[literals.Length + distances.Length];

                    int value = -1;
                    int length = 0;
                    int off = 0;
                    while (off < auxBuffer.Length)
                    {
                        if (length > 0)
                        {
                            auxBuffer[off++] = value;
                            length--;
                        }
                        else
                        {
                            int symbol = NextSymbol(reader, codeLengthTree);
                            if (symbol < 16)
                            {
                                value = symbol;
                                auxBuffer[off++] = value;
                            }
                            else
                            {
                                switch (symbol)
                                {
                                    case 16:
                                        length = (int)(ReadBits(reader, 2) + 3);
                                        break;
                                    case 17:
                                        value = 0;
                                        length = (int)(ReadBits(reader, 3) + 3);
                                        break;
                                    case 18:
                                        value = 0;
                                        length = (int)(ReadBits(reader, 7) + 11);
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }

                    Array.Copy(auxBuffer, 0, literals, 0, literals.Length);
                    Array.Copy(auxBuffer, literals.Length, distances, 0, distances.Length);
                }

            }

            private class BinaryTreeNode
            {
                private readonly int bits;
                public int literal = -1;
                public BinaryTreeNode leftNode;
                public BinaryTreeNode rightNode;

                private BinaryTreeNode(int bits)
                {
                    this.bits = bits;
                }

                void Leaf(int symbol)
                {
                    literal = symbol;
                    leftNode = null;
                    rightNode = null;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                BinaryTreeNode Left()
                {
                    if (leftNode == null && literal == -1)
                    {
                        leftNode = new BinaryTreeNode(bits + 1);
                    }
                    return leftNode;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                BinaryTreeNode Right()
                {
                    if (rightNode == null && literal == -1)
                    {
                        rightNode = new BinaryTreeNode(bits + 1);
                    }
                    return rightNode;
                }

                internal static BinaryTreeNode BuildTree(int[] litTable)
                {
                     int[] literalCodes = GetCodes(litTable);

                     BinaryTreeNode root = new BinaryTreeNode(0);

                    for (int i = 0; i < litTable.Length; i++)
                    {
                        int len = litTable[i];
                        if (len != 0)
                        {
                            BinaryTreeNode node = root;
                            int lit = literalCodes[len - 1];
                            for (int p = len - 1; p >= 0; p--)
                            {
                                 int bit = lit & (1 << p);
                                node = bit == 0 ? node.Left() : node.Right();
                                if (node == null)
                                {
                                    throw new InvalidDataException("node doesn't exist in Huffman tree");
                                }
                            }
                            node.Leaf(i);
                            literalCodes[len - 1]++;
                        }
                    }
                    return root;
                }

                private static int[] GetCodes(int[] litTable)
                {
                    int max = 0;
                    int[] blCount = new int[65];

                    foreach (int aLitTable in litTable)
                    {
                        if (aLitTable < 0 || aLitTable > 64)
                        {
                            throw new InvalidDataException("Invalid code " + aLitTable
                                + " in literal table");
                        }
                        max = Math.Max(max, aLitTable);
                        blCount[aLitTable]++;
                    }
                    //blCount = Arrays.copyOf(blCount, max + 1);
                    int[] blCountCopy = new int[max + 1];
                    Array.Copy(blCount, 0, blCountCopy, 0, max + 1);

                    int code = 0;
                    int[] nextCode = new int[max + 1];
                    for (int i = 0; i <= max; i++)
                    {
                        code = (code + blCountCopy[i]) << 1;
                        nextCode[i] = code;
                    }

                    return nextCode;
                }


            }

            #endregion

            #region "Private Class"

            internal class DecodingMemory
            {
                private readonly byte[] memory;
                private readonly int mask;
                private int wHead;

                public DecodingMemory()
                {
                    memory = new byte[1 << 16];
                    mask = memory.Length - 1;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public byte Add(byte b)
                {
                    memory[wHead] = b;
                    wHead = (wHead + 1) & mask;
                    return b;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void Add(byte[] b, int off, int len)
                {
                    for (int i = off; i < off + len; i++)
                    {
                        memory[wHead] = b[i];
                        wHead = (wHead + 1) & mask;
                    }
                }

                public void RecordToBuffer(int distance, int length, byte[] buff)
                {
                    if (distance > memory.Length)
                    {
                        throw new InvalidDataException("Illegal distance parameter: " + distance);
                    }
                    int start = (wHead - distance) & mask;
                    //if (!wrappedAround && start >= wHead)
                    //{
                    //    throw new InvalidDataException("Attempt to read beyond memory: dist=" + distance);
                    //}
                    for (int i = 0, pos = start; i < length; i++, pos = (pos + 1) & mask)
                    {
                        buff[i] = Add(memory[pos]);
                    }
                }

            }

            #endregion

            #region "constructor"

            internal HuffmanDecoder(Stream st)
            {
                this.reader = new BitInputStream(st);
                state = new InitialState(this.reader);
            }

            #endregion

            #region "Public Methods"

            public int Decode(byte[] b, int off, int len)
            {
                while (!finalBlock || state.HasData())
                {
                    if (state.State() == HuffmanState.INITIAL)
                    {
                        finalBlock = ReadBits(1) == 1;
                        int mode = (int)ReadBits(2);
                        switch (mode)
                        {
                            case 0:
#if DEBUG
                                //Debug.WriteLine("HuffmanDecoder SwitchToUncompressedState()");
#endif
                                SwitchToUncompressedState();
                                break;
                            case 1:
#if DEBUG
                                //Debug.WriteLine("HuffmanDecoder HuffmanState = FixedCodes");
#endif
                                state = new HuffmanCodes(HuffmanState.FIXED_CODES, FIXED_LITERALS, FIXED_DISTANCE, reader, memory);
                                break;
                            case 2:
#if DEBUG
                                //Debug.WriteLine("HuffmanDecoder HuffmanState = DynamicCodes");
#endif
                                int[][] tables = ReadDynamicTables();
                                state = new HuffmanCodes(HuffmanState.DYNAMIC_CODES, tables[0], tables[1], reader, memory);
                                break;
                            default:
                                throw new InvalidDataException("Unsupported compression: " + mode);
                        }
                    }
                    else
                    {
                        int r = state.Read(b, off, len);
                        if (r != 0)
                        {
#if DEBUG
                            //Debug.WriteLine($"HuffmanDecoder ReadBytes={r}");
#endif
                            return r;
                        }
                    }
                }
                return -1;
            }

            #endregion

            #region "Private Methods"

            private void SwitchToUncompressedState()
            {
                reader.AlignWithByteBoundary();
                long bLen = ReadBits(reader, 16);
                long bNLen = ReadBits(reader, 16);
                if (((bLen ^ 0xFFFF) & 0xFFFF) != bNLen)
                {
                    //noinspection DuplicateStringLiteralInspection
                    throw new InvalidDataException("Illegal LEN / NLEN values");
                }
                state = new UncompressedState(bLen, reader, memory);
            }

            private int[][] ReadDynamicTables()
            {
                int[][] result = new int[2][];
                int literals = (int)(ReadBits(5) + 257);
                result[0] = new int[literals];

                int distances = (int)(ReadBits(5) + 1);
                result[1] = new int[distances];

                HuffmanCodes.PopulateDynamicTables(reader, result[0], result[1]);
                return result;
            }

            private long ReadBits(int numBits)
            {
                return ReadBits(reader, numBits);
            }

            private static long ReadBits(BitInputStream reader, int numBits)
            {
                long r = reader.ReadBits(numBits);
                if (r == -1)
                {
                    throw new InvalidDataException("Truncated Deflate64 Stream");
                }
                return r;
            }

            #endregion

            #region "Dispose"

            private bool disposedValue;

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: マネージド状態を破棄します (マネージド オブジェクト)

                        this.reader = null;
                        this.memory = null;

                    }

                    // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                    // TODO: 大きなフィールドを null に設定します
                    disposedValue = true;
                }
            }

            // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
            // ~HuffmanDecoder()
            // {
            //     // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            //     Dispose(disposing: false);
            // }

            public void Dispose()
            {
                // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }

            #endregion

        }

        #endregion

        #region "Override Methods"

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0)
            {
                return 0;
            }
            int read = -1;
            if (decoder != null)
            {
                try
                {
                    read = decoder.Decode(buffer, offset, count);
                }
                catch (Exception ex) {
                    throw new InvalidDataException("Invalid Deflate64 input", ex);
                }
                //compressedBytesRead = decoder.GetBytesRead();
                //count(read);
                if (read == -1)
                {
                    return 0;
                }
            }
            return read;
            }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Close()
        {
            if (this.leaveOpen == false)
            {
                if (this.originalStream != null)
                {
                    try
                    {
                        this.originalStream.Close();
                    }
                    catch (Exception) { }

                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if(decoder != null)
                {
                    try
                    {
                        decoder.Dispose();

                    }
                    catch (Exception) { }
                    finally
                    {
                        decoder = null;
                    }
                }
                this.originalStream = null;
            }
            base.Dispose(disposing);
        }

        #endregion

    }
}

#endif