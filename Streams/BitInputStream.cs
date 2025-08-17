/*
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Numerics;

#if YGZIPLIB && DEFLATE64

namespace YGZipLib.Streams
{

    /// <summary>
    /// BitInputStream
    /// Apache Commons Compress Release 1.21から移植
    /// src/main/java/org/apache/commons/compress/utils
    ///   BitInputStream.java
    /// </summary>
    class BitInputStream : IDisposable
    {

        #region const

        private const int BYTE_SIZE = 8;

        private const int MAXIMUM_CACHE_SIZE = 63; // bits in long minus sign bit

        #endregion

        #region variables

        private static readonly ulong[] MASKS = new ulong[MAXIMUM_CACHE_SIZE + 1];

        private readonly byte[] buffer = new byte[4096];

        private readonly int bufLen = 4096;

        private int bufferPos = 0;

        private int bufferCached = 0;

        /// <summary>入力ストリーム</summary>
        private Stream inSt;

        /// <summary>出力キャッシュ</summary>
        /// <remarks>.netではjavaにある符号無し右シフトがないので、unsignedとしている</remarks>
        private ulong bitsCached = 0;

        /// <summary>キャッシュしているビット数</summary>
        private int bitsCachedSize = 0;

        #endregion

        #region constructor

        static BitInputStream()
        {
            for (int i = 1; i <= MAXIMUM_CACHE_SIZE; i++)
            {
                MASKS[i] = (MASKS[i - 1] << 1) + 1;
            }
        }

        public BitInputStream(Stream inSt)
        {
            this.inSt = inSt;
        }

        #endregion

        #region priperties

        public int BitsCached => this.bitsCachedSize;

        #endregion

        #region public methods

        public void ClearBitCache()
        {
            bitsCached = 0;
            bitsCachedSize = 0;
        }

        public long ReadBits(int count)
        {
            // ビット読み出し

            if (count < 0 || count > MAXIMUM_CACHE_SIZE)
            {
                throw new InvalidDataException("count must not be negative or greater than " + MAXIMUM_CACHE_SIZE);
            }
            if (EnsureCache(count))
            {
                return -1;
            }
            if (bitsCachedSize < count)
            {
                return ProcessBitsGreater57(count);
            }
            return ReadCachedBits(count);
        }

        public long BitsAvailable()
        {
            // 読み出せる残りビット数

            if (inSt.CanSeek == false)
            {
                throw new NotSupportedException();
            }
            return (long)bitsCachedSize + ((long)BYTE_SIZE) * ((inSt.Length - inSt.Position) + bufferCached);
        }

        public void AlignWithByteBoundary()
        {

            // あまりビットを切り捨てて、次のバイト境界に位置づける
            // Deflate64では未圧縮ブロックをByte配列で読み出す前にCallされる

            // 呼び出される場合は bitCached < 8 となるはず。
            //  bitsCached = 0; bitsCachedSize = 0;
            // でも良い？
#if DEBUG
            if (bitsCached >= 8)
            {
                throw new InvalidDataException($"BitInputStream.AlignWithByteBoundary  bitCached>=8!  bitCached={bitsCached}");
            }
#endif
            int toSkip = bitsCachedSize % BYTE_SIZE;
            if (toSkip > 0)
            {
                ReadCachedBits(toSkip);
            }
        }

        public int Read(byte[] buf, int off, int len)
        {

            // 元ストリームから直接読み出す
            // AlignWithByteBoundary() でバイト境界に位置づけしてから呼び出す

            // バッファが有る場合はまずはバッファから返却
            if (bufferCached > 0)
            {
                int copyLen = Math.Min(bufferCached, len);
                Array.Copy(buffer, bufferPos, buf, off, copyLen);
                bufferPos += copyLen;
                bufferCached -= copyLen;
                return copyLen;
            }
            return inSt.Read(buf, off, len);
        }

        #endregion

        #region private methods

        private long ReadBufferByte()
        {
            if (bufferCached == 0)
            {
                bufferCached = this.inSt.Read(buffer, 0, bufLen);
                bufferPos = 0;
            }
            if (bufferCached == 0)
                return -1;
            bufferCached--;
            return buffer[bufferPos++];

        }

        private long ProcessBitsGreater57(int count)
        {
            long bitsOut;
            int overflowBits;

            // EnsureCacheでは最大57～64bitの範囲までしかcacheに積めない
            // countの上限が63のため、不足分の調整をしている

            // 前提
            // count は 57～63(7bit)の範囲
            // bitCachedSize も 57～63 の範囲（※count > bitCachedSize なので実際には62以下）
            // count > bitCachedSize

            // bitsCachedSize >= 57 and left-shifting it 8 bits would cause an overflow
            // 不足しているbit数。不足するbit数最大は6bitのハズ
            int bitsToAddCount = count - bitsCachedSize;

            // 追加で読み込んでcacheに積むbit数 bitsToAddCount + overflowBits = 8
            overflowBits = BYTE_SIZE - bitsToAddCount;

            // read
            long nextByte = this.ReadBufferByte();
            if (nextByte < 0)
            {
                return nextByte;
            }
            ulong uNextByte = (ulong)nextByte;

            // 不足分をcacheに追加
            ulong bitsToAdd = uNextByte & MASKS[bitsToAddCount];
            bitsCached |= (bitsToAdd << bitsCachedSize);

            // 返却値設定(MASK不要では？)
            bitsOut = (long)(bitsCached & MASKS[count]);

            // あまり分をcacheに設定
            bitsCached = (uNextByte >> bitsToAddCount) & MASKS[overflowBits];
            bitsCachedSize = overflowBits;

            return bitsOut;
        }

        private long ReadCachedBits(int count)
        {

            // cacheからの読込
            // 読み出し後の bitsCachedSize は7bit以下になるハズ

            ulong bitsOut;
            bitsOut = (bitsCached & MASKS[count]);
            bitsCached >>= count;
            bitsCachedSize -= count;
            return (long)bitsOut;
        }

        private Boolean EnsureCache(int count)
        {

            // count < bitsCachedSize の場合、count >= bitsCachedSize になるまでcacheに積む
            // 但しcacheには8bit単位でしか積めないので、chche済が8bit境界で無い場合、57～63bitまでしか積めない。
            // 8bit境界の場合は最大64bitまでcacheに積める
            // 最大まで積んでも端数bitの都合で count > bitsCachedSize となる場合は ProcessBitsGreater57 で対応。
            long nextByte;
            while (bitsCachedSize < count && bitsCachedSize < 57)
            {
                nextByte = this.ReadBufferByte();
                if (nextByte < 0)
                {
                    return true;
                }
                bitsCached |= ((ulong)nextByte << bitsCachedSize);
                bitsCachedSize += BYTE_SIZE;
            }
            return false;
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
                    inSt = null;
                }

                // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // TODO: 大きなフィールドを null に設定します
                disposedValue = true;
            }
        }

        // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
        // ~BitInputStream()
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

}

#endif