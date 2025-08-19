using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;

#if YGZIPLIB
using YGZipLib.Properties;
using YGZipLib.Common;

namespace YGZipLib.Streams
{

    /// <summary>
    /// UnZipをマルチスレッドで実行するためのストリーム管理クラス
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

        private const int READ_BUFFER_SIZE = 8192;

        private readonly ConcurrentDictionary<string, Stream> streamDic;
		private readonly object lockObject = new object();
		//private readonly FileStream tempFileStream;
		private readonly byte[] zipBytes;
		private readonly string zipFileName;
		private readonly TEMP_MODE mode;

		internal int StreamCount => streamDic.Count;

        #region "private class"

		private class TempFileStream : FileStream
		{
			public bool InUse { get; set; } = true;
			public string Id { get; } = Guid.NewGuid().ToString("N");
            public TempFileStream(string zipFileName) : base(zipFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, READ_BUFFER_SIZE, FileOptions.RandomAccess | FileOptions.Asynchronous) { }
//            public override void Close()
//            {
//#if DEBUG
//                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : TempFileStream Close: Id={Id}");
//#endif
//                base.Close();
//            }
        }

        private class TempMemoryStream : MemoryStream
		{
            public bool InUse { get; set; } = true;
			public string Id { get; }= Guid.NewGuid().ToString("N");
            public TempMemoryStream(byte[] zipData) : base(zipData, false) { }
//            public override void Close()
//            {
//#if DEBUG
//                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : TempMemoryStream Close: Id={Id}");
//#endif
//                base.Close();
//            }
        }

        private class TempStream : Stream
		{
			private readonly Stream baseStream;

            private readonly CloseStreamDelegate CloseStream;

            private readonly TEMP_MODE tempMode;

            /// <summary>CloseStream の二重実行防止</summary>
            private int closed = 0;

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
                if (Interlocked.Exchange(ref closed, 1) == 0)
                {
                    CloseStream(this);
                }
                base.Close();
            }

            private bool disposedValue = false;

            protected override void Dispose(bool disposing)
            {
                if (disposedValue == false)
                {
                    if (disposing)
                    {
                        if (Interlocked.Exchange(ref closed, 1) == 0)
                        {
                            CloseStream(this);
                        }
                    }
                    base.Dispose(disposing);
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
            if(Directory.Exists(tempDir) == false)
            {
                throw new DirectoryNotFoundException(string.Format(Resources.ERRMSG_TEMPDIR_NOTFOUND, tempDir));
            }
            streamDic = new ConcurrentDictionary<string, Stream>();
			mode = TEMP_MODE.STREAM;
            zipFileName = ShareMethodClass.GetTempFileName(tempDir);
            using (FileStream fs = new FileStream(zipFileName, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 819200, FileOptions.SequentialScan))
            {
                zipStream.CopyTo(fs);
                fs.Flush();
            }
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
                foreach (Stream st in streamDic.Values)
                {
                    if (st is TempFileStream fs)
                    {
                        if (fs.InUse == false) { 
                            zst = st;
                            break;
                        }
                    }
                    else
                    {
                        TempMemoryStream ms = (TempMemoryStream)st;
                        if (ms.InUse == false) { 
                            zst = st;
                            break;
                        }
                    }
                }

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

                // ストリームを使用中に設定
                ts.InUse = true;

                // Positionは必ず設定されるので、0に設定しない。
                // ts.Position = 0;

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
                // ストリームを閉じる
                try { ts.Value.Dispose(); } catch { }
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

                    if (mode == TEMP_MODE.STREAM)
                    {
                        if (File.Exists(this.zipFileName))
                        {
#if DEBUG
                            try { File.Delete(this.zipFileName); }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to delete temporary file: {this.zipFileName}, Exception: {ex.Message}");
                            }
#else
                            try { File.Delete(this.zipFileName); } catch { }
#endif
                        }
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