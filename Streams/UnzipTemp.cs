using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

#if YGZIPLIB

namespace YGZipLib.Streams
{

    /// <summary>
    /// UnZipをマルチスレッドで実行するためのストリーム
    /// </summary>
    internal class UnzipTemp : IDisposable
	{
		private delegate void CloseStreamDelegate(Stream tempStream);

		private enum TEMP_MODE
		{
			FILE,
			STREAM,
			BYTES
		}

		private readonly ConcurrentDictionary<string, Stream> streamDic;
		private readonly object lockObject = new object();
		private readonly FileStream tempFileStream;
		private readonly byte[] zipBytes;
		private readonly string zipFileName;
		private readonly TEMP_MODE mode;

		internal int StreamCount => streamDic.Count;

        #region "private class"

		private class TempFileStream : FileStream
		{
			public bool InUse { get; set; } = true;
			public string Id { get; } = Guid.NewGuid().ToString("N");
            public TempFileStream(string zipFileName) : base(zipFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.RandomAccess | FileOptions.Asynchronous) { }
            public override void Close()
            {
#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : TempFileStream Close: Id={Id}");
#endif
                base.Close();
            }
        }

        private class TempMemoryStream : MemoryStream
		{
            public bool InUse { get; set; } = true;
			public string Id { get; }= Guid.NewGuid().ToString("N");
            public TempMemoryStream(byte[] zipData) : base(zipData, false) { }
            public override void Close()
            {
#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : TempMemoryStream Close: Id={Id}");
#endif
                base.Close();
            }
        }

        private class TempStream : Stream
		{
			private readonly Stream baseStream;

            private readonly CloseStreamDelegate CloseStream;

            private readonly TEMP_MODE tempMode;
            
            public string Id { get
				{
                    switch (tempMode)
                    {
                        case TEMP_MODE.BYTES:
                            TempMemoryStream ms = (TempMemoryStream)baseStream;
                            return ms.Id;
                        default:
                            TempFileStream fs = (TempFileStream)baseStream;
                            return fs.Id;
                    }
                }
            }

			public bool InUse {
                get
				{
                    switch (tempMode)
                    {
                        case TEMP_MODE.BYTES:
                            TempMemoryStream ms = (TempMemoryStream)baseStream;
                            return ms.InUse;
                        default:
                            TempFileStream fs = (TempFileStream)baseStream;
                            return fs.InUse;

                    }
                }
                set
                {
                    switch (tempMode)
                    {
                        case TEMP_MODE.BYTES:
                            TempMemoryStream ms = (TempMemoryStream)baseStream;
                            ms.InUse = value;
                            break;
                        default:
                            TempFileStream fs = (TempFileStream)baseStream;
                            fs.InUse = value;
                            break;

                    }
                }
            }

            internal TempStream(TEMP_MODE tempMode, Stream baseStream, CloseStreamDelegate cs)
            {
                this.baseStream = baseStream;
				this.CloseStream = cs;
                this.tempMode = tempMode;
			}

			public override bool CanRead => baseStream.CanRead;

			public override bool CanSeek => baseStream.CanSeek;

			public override bool CanWrite => false;

			public override long Length => baseStream.Length;

			public override long Position
			{
				get
				{
					return baseStream.Position;
				}
				set
				{
					baseStream.Position = value;
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
				return baseStream.Read(buffer, offset, count);
			}

			public override long Seek(long offset, SeekOrigin origin)
			{
				return baseStream.Seek(offset, origin);
			}

            public override void Close()
            {
				CloseStream(this);
                base.Close();
            }

            private bool disposedValue = false;

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposedValue == false)
                {
					disposedValue = true;
                }
            }

        }

		#endregion

		#region "コンストラクタ"

		public UnzipTemp(string zipFileName)
		{
			streamDic = new ConcurrentDictionary<string, Stream>();
			mode = TEMP_MODE.FILE;
			this.zipFileName = zipFileName;
		}

		public UnzipTemp(Stream zipStream, string tempDir)
		{
			streamDic = new ConcurrentDictionary<string, Stream>();
			mode = TEMP_MODE.STREAM;
            zipFileName = Path.Combine(tempDir ?? Path.GetTempPath(), $"zltmp_{Guid.NewGuid():N}_{Guid.NewGuid():N}.tmp");
            tempFileStream = new FileStream(zipFileName, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 8192, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
			zipStream.CopyTo(tempFileStream);
			tempFileStream.Flush();
		}

		public UnzipTemp(byte[] zipBytes)
		{
			streamDic = new ConcurrentDictionary<string, Stream>();
			mode = TEMP_MODE.BYTES;
			this.zipBytes = zipBytes;
		}

		#endregion

        #region "Methods"

        internal Stream GetZipStream()
		{
			Stream zst = null;
			lock (lockObject)
			{
                zst = streamDic.Values.ToList().Find(st => {
                    if (st is TempFileStream fs)
                    {
                        if (fs.InUse == false) { return true; }
                    }
                    else
                    {
                        TempMemoryStream ms = (TempMemoryStream)st;
                        if (ms.InUse == false) { return true; }
                    }
                    return false;
                });

                TempStream ts = null;
                if (zst == null)
                {
                    switch (this.mode)
                    {
                        case TEMP_MODE.BYTES:
                            zst = new TempMemoryStream(this.zipBytes);
                            break;
                        default:
                            zst = new TempFileStream(this.zipFileName);
                            break;
                    }
                    ts = new TempStream(this.mode, zst, CloseTempStream);
                    streamDic.TryAdd(ts.Id, zst);
                }
                else
                {
                    ts = new TempStream(this.mode, zst, CloseTempStream);
                }
                ts.InUse = true;
#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetZipStream: Id={ts.Id} streamDic.Count={streamDic.Count}");
#endif
                return ts;
            }
		}

		private void CloseTempStream(Stream tempStream)
		{
			TempStream ts=(TempStream)tempStream;
#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : CloseTempStream: Id={ts.Id} streamDic.Count={streamDic.Count}");
#endif
            ts.InUse = false;
        }

        private void DisposeAllStream()
        {
            foreach (var ts in streamDic)
            {
				ts.Value.Dispose();
            }
			streamDic.Clear();
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

					DisposeAllStream();

                    if(mode== TEMP_MODE.STREAM)
                    {
                        tempFileStream?.Dispose();
                    }

                }

                // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // TODO: 大きなフィールドを null に設定します
                disposedValue = true;
            }
        }

        // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
        // ~UnzipTemp()
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