using System;
using System.IO;

#if YGZIPLIB
namespace YGZipLib.Streams
#elif YGMAILLIB
namespace YGMailLib.Zip.Streams
#endif
{
    /// <summary>
    /// 出力件数カウントストリーム
    /// </summary>
    /// <remarks></remarks>
    internal class WriteCountStream : Stream
	{
		private long writeCount = 0;
		private Stream writeStream = null;

		#region "コンストラクタ"

		public WriteCountStream(Stream writeStream)
		{
			if (writeStream == null)
			{
				throw new ArgumentNullException(nameof(writeStream));
            }
            this.writeStream = writeStream;
		}

		#endregion

		public long WriteCount => writeCount;

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => writeStream.Length;

		public override long Position
		{
			get
			{
				return this.writeStream.Position;
			}
			set
			{
				throw new NotSupportedException();
			}
		}

		public override void Flush()
		{
			writeStream.Flush();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			writeCount += count;
			writeStream.Write(buffer, offset, count);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
            if (disposing)
            {
                // writeStreamのDisposeは呼び出し元で行うため、ここではnullに設定するだけ
                writeStream = null;
            }
            base.Dispose(disposing);
        }

    }
}
