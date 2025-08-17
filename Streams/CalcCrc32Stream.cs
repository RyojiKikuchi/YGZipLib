using System;
using System.Diagnostics;
using System.IO;
#if YGZIPLIB
using YGZipLib.Common;
using YGZipLib.Streams;
#elif YGMAILLIB
using YGMailLib.Zip.Common;
using YGMailLib.Zip.Streams;
#endif

#if YGZIPLIB
namespace YGZipLib.Streams
#elif YGMAILLIB
namespace YGMailLib.Zip.Streams
#endif
{
	/// <summary>
	/// CRC32算出クラス
	/// </summary>
	/// <remarks></remarks>
	internal class CalcCrc32Stream : Stream
	{
		public enum StreamMode : int
		{
			READ = 0,
			WRITE = 1
		}
		private unsafe struct FixedBuffer
		{
            public fixed UInt32 crcTable[256];
        }

        private FixedBuffer fixedBuf = default;
		
		private UInt32 crc32Work = UInt32.MaxValue;
		private Stream baseStream = null;
		private readonly StreamMode streamMode;
		private long ioCount = 0;

        #region "Properties"

		/// <summary>
		/// 算出したCRC32取得
		/// </summary>
		/// <value></value>
		/// <returns></returns>
		/// <remarks></remarks>
		public uint Crc32
        {
            get
            {
				return ~crc32Work;
			}
        }

		public long IoCount
		{
			get
			{
				return ioCount;
			}
		}

        #endregion

        #region "コンストラクタ"

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <remarks></remarks>
		public unsafe CalcCrc32Stream(Stream baseStream, StreamMode streamMode)
		{
			if (baseStream == null)
			{
				throw new ArgumentNullException(nameof(baseStream));
            }
            this.baseStream = baseStream;
			this.streamMode = streamMode;
            fixed (UInt32* crcTable = fixedBuf.crcTable)
            {
                ShareMethodClass.CopyCrcTable(crcTable);
            }

        }

        #endregion

        #region static methods

        internal static uint CalcCrc(byte[] bytes)
        {
            using (Stream ns = Stream.Null)
            using (CalcCrc32Stream calcCrc = new CalcCrc32Stream(ns, CalcCrc32Stream.StreamMode.WRITE))
            {
                calcCrc.Write(bytes, 0, bytes.Length);
                return calcCrc.Crc32;
            }
        }

        #endregion

        #region "Methods"

        /// <summary>
        /// CRC32算出結果のリセット
        /// </summary>
        /// <remarks></remarks>
        public void ResetCrc32()
		{
			crc32Work = UInt32.MaxValue;
		}

        #endregion

        #region "Override Methods"

		public override bool CanRead => streamMode == StreamMode.READ;

		public override bool CanSeek => false;

		public override bool CanWrite => streamMode == StreamMode.WRITE;

		public override long Length => baseStream.Length;

		public override long Position
		{
			get
			{
				return this.baseStream.Position;
			}
			set
			{
				throw new NotSupportedException();
			}
		}

		public override void Flush()
		{
			if (this.streamMode == StreamMode.WRITE)
			{
				this.baseStream.Flush();
			}
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			if (streamMode == StreamMode.READ)
			{
				throw new NotSupportedException();
			}
            ioCount += count;
            unsafe
            {
				fixed(byte* bp = buffer)
				{
                    byte* bop = &bp[offset];
                    for (int i = 0; i < count; i++)
                    {
                        crc32Work = fixedBuf.crcTable[(crc32Work ^ *(bop++)) & 0xFF] ^ (crc32Work >> 8);
                    }
                }
            }

			baseStream.Write(buffer, offset, count);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (streamMode == StreamMode.WRITE)
			{
				throw new NotSupportedException();
			}
			
			int readCount = baseStream.Read(buffer, offset, count);
			ioCount += readCount;
            unsafe
            {
                fixed (byte* bp = buffer)
                {
					byte* bop = &bp[offset];
                    for (int i = 0; i < readCount; i++)
                    {
                        crc32Work = fixedBuf.crcTable[(crc32Work ^ *(bop++)) & 0xFF] ^ (crc32Work >> 8);
                    }
                }
            }
			return readCount;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
                // baseStreamのDisposeはここでは行わない
                baseStream = null;
			}
			base.Dispose(disposing);
		}

        #endregion

	}
}
