using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
#if YGZIPLIB
using YGZipLib.Common;
namespace YGZipLib.Streams
#elif YGMAILLIB
using YGMailLib.Zip.Common;
namespace YGMailLib.Zip.Streams
#endif
{
    /// <summary>
    /// テンポラリストリーム管理クラス
    /// </summary>
    /// <remarks>
    /// 管理クラスをインポートしてGetTempStreamで<see cref="AutoTemporaryStream"/>を取得する
    /// <see cref="AutoTemporaryStream"/>は使用終了後にClose/Disposeすること
    /// </remarks>
    internal class TempStreamManage : IDisposable
    {

        private delegate void CloseTempFileStreamDelegate(TempFileStream st);
        private delegate Stream GetTempFileStreamDelegate();
        private readonly string id = Guid.NewGuid().ToString("N");

        /// <summary>
        /// ファイルストリームのバッファサイズ
        /// </summary>
        private const int FILESTREAM_BUFFER_SIZE = 16384;

        /// <summary>
        /// tempFileListをロックするためのオブジェクト
        /// </summary>
        private readonly object lockObject = new object();

        /// <summary>
        /// 一時ファイルのパス
        /// </summary>
        private readonly string tempDirPath;

        /// <summary>
        /// TempFileStreamを管理するためのリスト
        /// </summary>
        private readonly ConcurrentBag<TempFileStream> tempFileList = new ConcurrentBag<TempFileStream>();

        #region "コンストラクタ"

        public TempStreamManage(string tempDirPath)
        {
            this.tempDirPath = tempDirPath;
        }

        #endregion

        #region "internal class"

        internal class TempFileStream : FileStream
        {

            /// <summary>
            /// 使用中
            /// </summary>
            /// <returns></returns>
            public bool InUse { get; set; } = false;

            private readonly string tempFileName;

            public TempFileStream(string tempFileName)
                : base(tempFileName, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FILESTREAM_BUFFER_SIZE, FileOptions.SequentialScan | FileOptions.Asynchronous)
            {
                this.tempFileName = tempFileName;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    if (File.Exists(tempFileName))
                    {
                        try
                        {
                            File.Delete(tempFileName);
                        }
#if DEBUG
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to delete temporary file: {tempFileName}, Exception: {ex.Message}");
                        }
#else
                        catch { }
#endif
                }
                }
            }

        }

        private class AutoTemporaryStream : Stream
        {
            private enum TempStreamMode
            {
                MEMORY,
                FILE
            }

            private readonly CloseTempFileStreamDelegate CloseTempFileStream;
            private readonly GetTempFileStreamDelegate GetTempFileStream;

            /// <summary>
            /// テンポラリストリーム
            /// </summary>
            private Stream tempStream = null;

            /// <summary>
            /// MemoryStreamの最大サイズ。このサイズを超えるとファイルストリームに切り替える
            /// </summary>
            private const long MAX_MEMORY_STREAM_SIZE = 65536L;

            private TempStreamMode StreamMode { get; set; } = TempStreamMode.MEMORY;
            
            #region "コンストラクタ"

            internal AutoTemporaryStream(GetTempFileStreamDelegate gtfs, CloseTempFileStreamDelegate ctfs, long estimateSize)
            {
                GetTempFileStream = gtfs;
                CloseTempFileStream = ctfs;
                if (estimateSize <= MAX_MEMORY_STREAM_SIZE)
                {
                    tempStream = new MemoryStream();
                }
                else
                {
                    tempStream = GetTempFileStream();
                    StreamMode = TempStreamMode.FILE;
                }
            }

            #endregion

            /// <summary>
            /// メモリストリームからファイルストリームに変更
            /// </summary>
            private void SwitchToFileStream()
            {
                Stream fs = GetTempFileStream();
                tempStream.Flush();
                tempStream.Position = 0L;
                ((MemoryStream)tempStream).CopyTo(fs);
                tempStream.Dispose();
                tempStream = fs;
                StreamMode = TempStreamMode.FILE;
            }

            public override bool CanRead => tempStream.CanRead;

            public override bool CanSeek => tempStream.CanSeek;

            public override bool CanWrite => tempStream.CanWrite;

            public override long Length => tempStream.Length;

            public override long Position
            {
                get
                {
                    return tempStream.Position;
                }
                set
                {
                    tempStream.Position = value;
                }
            }



            public override void Flush()
            {
                tempStream.Flush();
            }

            public override void SetLength(long value)
            {
                tempStream.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (StreamMode == TempStreamMode.MEMORY && (tempStream.Length + count) > MAX_MEMORY_STREAM_SIZE)
                {
                    SwitchToFileStream();
                }
                tempStream.Write(buffer, offset, count);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return tempStream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return tempStream.Seek(offset, origin);
            }

            public override void Close()
            {
                if (StreamMode == TempStreamMode.FILE)
                {
                    CloseTempFileStream((TempFileStream)tempStream);
                }
                else
                {
                    tempStream.Close();
                }
                base.Close();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (StreamMode != TempStreamMode.FILE)
                    {
                        tempStream.Dispose();
                    }
                    tempStream = null;
                }
                base.Dispose(disposing);
            }
        }

        #endregion

        #region "Private Methods"

        /// <summary>
        /// TemporaryFileStreamの取得
        /// </summary>
        /// <returns></returns>
        private TempFileStream GetTempFileStream()
        {
            TempFileStream fs = OpenCloseList(null);
            if (fs != null)
            {
                return fs;
            }

            // テンポラリファイル作成
            fs = new TempFileStream(ShareMethodClass.GetTempFileName(tempDirPath))
            {
                InUse = true
            };

            tempFileList.Add(fs);
#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetTempFileStream() Count={tempFileList.Count}");
#endif
            return fs;
        }

        /// <summary>
        /// ストリームをクローズする
        /// </summary>
        /// <param name="st"></param>
        private void CloseTempFileStream(Stream st)
        {
            TempFileStream tfs = (TempFileStream)st;
            tfs.SetLength(0L);
            tfs.InUse = false;
        }

        private TempFileStream OpenCloseList(TempFileStream stream)
        {
            lock (lockObject)
            {
                if (stream != null)
                {
                    stream.SetLength(0L);
                    stream.InUse = false;
                    return stream;
                }
                foreach (TempFileStream item in tempFileList)
                {
                    if (!item.InUse)
                    {
                        item.InUse = true;
                        return item;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 全ファイルストリームを開放する
        /// </summary>
        private void DisposeAllStream()
        {
            foreach (TempFileStream tfs in tempFileList)
            {
                tfs.Dispose();
            }
        }

        //public Stream GetTempStream()
        //{
        //    return GetTempStream(0L);
        //}

        public Stream GetTempStream(long estimateSize)
        {
            return new AutoTemporaryStream(GetTempFileStream, CloseTempFileStream, estimateSize);
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
                }

                // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // TODO: 大きなフィールドを null に設定します
                disposedValue = true;
            }
        }

        // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
        // ~TempStreamManage()
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
    }

    #endregion

}
