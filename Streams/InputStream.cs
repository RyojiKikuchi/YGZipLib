using System;
using System.Diagnostics;
using System.IO;

#if YGZIPLIB
namespace YGZipLib.Streams
#elif YGMAILLIB
namespace YGMailLib.Zip.Streams
#endif
{
    /// <summary>
    /// InputStream
    /// 元ストリームの指定範囲を独立したInputStreamのように取り扱えるStream
    /// ※他から元ストリームのPositionを変更するような操作を行わないこと
    /// </summary>
    internal class InputStream : Stream
	{
		private Stream baseStream = null;
		private readonly long initPos = 0L;
		private readonly long maxPos = 0L;

		internal Stream BaseStream { get { return baseStream; } }

		#region "コンストラクタ"

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="inputStream"></param>
		/// <remarks>
		/// 元となるストリーム全体をInputStreamとする
		/// </remarks>
		public InputStream(Stream inputStream)
		{
			CheckBaseStream(inputStream);
			baseStream = inputStream;
			initPos = 0L;
			maxPos = baseStream.Length;
		}

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="inputStream"></param>
		/// <param name="inputLength"></param>
		/// <remarks>
		/// 元となるストリームの現在位置から指定された長さ分をInputStreamとする
		/// </remarks>
		public InputStream(Stream inputStream, long inputLength)
		{
			CheckBaseStream(inputStream);
			baseStream = inputStream;
			initPos = inputStream.Position;
			maxPos = initPos + inputLength;
			if (maxPos > baseStream.Length)
			{
				throw new IOException("inputLength is out of stream range.");
			}
		}

		#endregion

		#region "Methods"

		/// <summary>
		/// 元ストリームから指定されたバッファ長分読み込む。途中でストリームの終わりになった場合、例外が発生する。
		/// </summary>
		/// <param name="buffer"></param>
		/// <remarks></remarks>
		public void ReadBuffer(byte[] buffer)
		{
			int readPointer = 0;
			do
			{
				int readCount = baseStream.Read(buffer, readPointer, buffer.Length - readPointer);
				if (readCount == 0)
				{
					throw new EndOfStreamException();
				}
				readPointer += readCount;
			}
			while (readPointer < buffer.Length);
		}

		/// <summary>
		/// 元ストリームのチェック
		/// </summary>
		/// <param name="baseStream"></param>
		private static void CheckBaseStream(Stream baseStream)
		{
			if (!baseStream.CanRead)
			{
				throw new ArgumentException("The stream cannot be read.");
			}
			if (!baseStream.CanSeek)
			{
				throw new ArgumentException("The stream cannot be seek.");
			}
		}

		#endregion

		#region "Override Methods"

		public override bool CanRead
        {
            get
            {
				return baseStream.CanRead;
			}
		} 

		public override bool CanSeek
		{
            get
            {
				return baseStream.CanSeek;
			}
		}

		public override bool CanWrite
        {
            get
            {
				return false;
			}
        }

		public override long Length
		{
            get
            {
				return (maxPos - initPos);
			}
		}

		public override long Position
		{
			get
			{
				return baseStream.Position - initPos;
			}
			set
			{
                if (value < 0 || value > this.Length)
                {
                    throw new IOException("Position is out of the valid range for this InputStream.");
                }
				baseStream.Position = initPos + value;
			}
		}


		public override void Flush()
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

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (baseStream.Position >= maxPos)
			{
				return 0;
			}

			long remain = this.Length - this.Position;
			int readCount = (int)((count > remain) ? remain : count);
            return baseStream.Read(buffer, offset, readCount);
        }

		public override long Seek(long offset, SeekOrigin origin)
		{
			long position = offset;
            switch (origin)
            {
				case SeekOrigin.Begin:
					position += this.initPos;
					break;
				case SeekOrigin.Current:
					position += this.baseStream.Position;
					break;
				case SeekOrigin.End:
					position += this.maxPos;
					break;
				default:
					throw new System.ArgumentOutOfRangeException(nameof(origin));
			}

            if (position < initPos || position > maxPos)
			{
				throw new IOException("Position is out of the valid range for this InputStream.");
            }
			this.baseStream.Position = position;
            return this.Position;
        }

        protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
                // 元ストリームはInputStreamの所有物ではないので、Disposeしない
                this.baseStream = null;
			}
			base.Dispose(disposing);
		}

		#endregion

	}
}
