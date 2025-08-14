using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;


#if YGZIPLIB
using YGZipLib.Common;
using YGZipLib.Streams;
#elif YGMAILLIB
using YGMailLib.Zip.Common;
using YGMailLib.Zip.Streams;
#endif
#if NET5_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Serialization.Formatters;
#endif

#if YGZIPLIB
namespace YGZipLib.Streams
#elif YGMAILLIB
namespace YGMailLib.Zip.Streams
#endif
{

    /// <summary>
    /// ZIP AES暗号化クラス
    /// </summary>
    /// <remarks></remarks>
    internal class ZipAesCryptStream : Stream, IDisposable
    {

        #region "ENUM"

        /// <summary>
        /// ストリームモード
        /// </summary>
        public enum StreamMode
        {
            /// <summary>暗号化</summary>
            ENCRYPT,
            /// <summary>復号化</summary>
            DECRYPT
        }

        #endregion

        #region "CONST"

        /// <summary>最大のマスク配列サイズ(16の倍数で指定)</summary>
        private const int MASK_SIZE = 32768;

        #endregion

        #region "メンバ変数"

        /// <summary>SALT長定義</summary>
        private static readonly int[] AES_SALT_LENGTH = new int[4] { 0, 8, 12, 16 };

        /// <summary>KEY長定義</summary>
        private static readonly int[] AES_KEY_LENGTH = new int[4] { 0, 16, 24, 32 };

        /// <summary>出力用Stream</summary>
        private Stream writeStream = null;

        /// <summary>入力用Stream</summary>
        private readonly InputStream readStream = null;

        /// <summary>読み込んだバイト数</summary>
        private long totalReadCount = 0;

        /// <summary>HMAC計算用</summary>
        private HMACSHA1 calcHmac = null;

        /// <summary>Streamモード</summary>
        private readonly StreamMode streamMode;

        /// <summary>暗号化用マスク配列</summary>
        private byte[] aesMaskBytes;

        /// <summary>マスク用配列ポインタ</summary>
        private int aesMaskPosition = 0;

        ///// <summary>マスク配列サイズ</summary>
        //private readonly int maskSize = (int)MASK_SIZE;

        /// <summary>マスク作成タスク</summary>
        private readonly AesMaskThread aesMaskThread = null;


#if NET7_0_OR_GREATER

        /// <summary>Vector512サポート判定</summary>
        private static readonly bool vector512Supported = Vector512.IsHardwareAccelerated;

        /// <summary>Vector256サポート判定</summary>
        private static readonly bool vector256Supported = Vector256.IsHardwareAccelerated;

        /// <summary>Vector128サポート判定</summary>
        private static readonly bool vector128Supported = Vector128.IsHardwareAccelerated;

#elif NET5_0_OR_GREATER

        /// <summary>AVX2サポート判定</summary>
        private static readonly bool avx2Supported = Avx2.IsSupported;

        /// <summary>AdvSimdサポート判定</summary>
        private static readonly bool advSimdSupported = AdvSimd.IsSupported;

#endif

#if DEBUG
        private readonly double constructorExecTime = 0;
        private double getNextMaskWaitTime = 0;
        private double readWriteTime = 0;
        private long readWriteCount = 0;
        private readonly Stopwatch readWriteStp = Stopwatch.StartNew();
#endif

        #endregion

        #region "コンストラクタ"

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseStream"></param>
        /// <param name="aesMode"></param>
        /// <param name="password"></param>
        /// <param name="streamMode"></param>
        /// <param name="length"></param>
        /// <remarks></remarks>
        public ZipAesCryptStream(Stream baseStream, ZipArcClass.ENCRYPTION_OPTION aesMode, byte[] password, StreamMode streamMode, long length)
        {
#if DEBUG
            Stopwatch stp = Stopwatch.StartNew();

#if NET8_0_OR_GREATER
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream: Constructor length={length}, vector512Supported={vector512Supported}, vector256Supported={vector256Supported}, vector128Supported={vector128Supported}");
#elif NET5_0_OR_GREATER
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream: Constructor length={length}, avx2Supported={avx2Supported}, advSimdSupported={advSimdSupported}");
#else
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream: Constructor length={length}");
#endif

#endif

            // マスク配列作成スレッド取得
            aesMaskThread = AesMaskThreadPool.GetThread();

            try
            {
                aesMaskPosition = MASK_SIZE;
                // ストリームモード設定
                this.streamMode = streamMode;

                // Saltサイズ取得
                switch (aesMode)
                {
                    case ZipArcClass.ENCRYPTION_OPTION.AES128:
                    case ZipArcClass.ENCRYPTION_OPTION.AES192:
                    case ZipArcClass.ENCRYPTION_OPTION.AES256:
                        break;
                    default:
                        throw new CryptographicException(nameof(aesMode), "Invalid AES mode specified.");
                }
                byte[] aesSalt = new byte[AES_SALT_LENGTH[(int)aesMode]];

                // ストリームモード別処理
                if (this.streamMode == StreamMode.ENCRYPT)
                {
                    // 書込ストリーム設定
                    writeStream = baseStream;

                    // SALT設定
                    using (RandomNumberGenerator randomGen = RandomNumberGenerator.Create())
                    {
                        randomGen.GetBytes(aesSalt);
                    }

                    // キー初期化
                    InitKey(aesMode, password, aesSalt);
                }
                else
                {
                    // 読込ストリーム設定
                    InputStream baseInputStream = (InputStream)baseStream;

                    // SALT取得
                    baseInputStream.ReadBuffer(aesSalt);

                    // パスワード検証値取得
                    byte[] passwordValidationDataIn = new byte[2];
                    baseInputStream.ReadBuffer(passwordValidationDataIn);

                    // キー初期化
                    byte[] passwordValidationDataInit = InitKey(aesMode, password, aesSalt);

                    // パスワード検証値チェック
                    if (!ShareMethodClass.ByteArrayCompare(passwordValidationDataIn, passwordValidationDataInit))
                    {
                        throw new CryptographicException("AES Password invalid");
                    }

                    // ストリーム設定
                    readStream = new InputStream(baseInputStream, baseInputStream.Length - baseInputStream.Position - 10);
                }

                //// マスク配列作成処理初回起動
                //aesMaskThread.Start();

            }
            catch (Exception)
            {
                aesMaskThread?.Dispose();
                aesMaskThread = null;
                throw;
            }

#if DEBUG
            constructorExecTime = stp.Elapsed.TotalMilliseconds;
#endif
        }

        #endregion

        #region AesMaskThreadPool

        /// <summary>
        /// マスク配列作成スレッドプール
        /// </summary>
        private static class AesMaskThreadPool
        {

            /// <summary>マスク配列作成処理格納リスト</summary>
            private static readonly List<AesMaskThread> threadList = new List<AesMaskThread>();

            /// <summary>マスク配列作成処理格納リストロック</summary>
            private static readonly object threadListLocker = new object();

            /// <summary>未使用スレッド回収タイマー</summary>
            private static readonly System.Timers.Timer tm = null;

            /// <summary>
            /// コンストラクタ
            /// </summary>
            static AesMaskThreadPool()
            {
                // 10秒ごとに回収処理を起動
                tm = new System.Timers.Timer(10000);
                tm.AutoReset = true;
                tm.Elapsed += Tm_Elapsed;
            }

            /// <summary>
            /// スレッド取得
            /// </summary>
            /// <returns></returns>
            public static AesMaskThread GetThread()
            {

                AesMaskThread thread;
                lock (threadListLocker)
                {

                    // 未使用スレッド取得
                    thread = threadList.Find(t => t.IsUnUsed == true);
                    
                    // 未使用スレッドが存在しない場合新規スレッドを作成
                    if (thread == null)
                    {
                        thread = new AesMaskThread();
                        threadList.Add(thread);
                    }

                    // 使用中フラグ設定
                    thread.InUse = true;

#if DEBUG
                    Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(GetThread): ManagedThreadId={thread.ManagedThreadId:x8}, Name={thread.Name}, Count={threadList.Count}, InUse={threadList.FindAll(t => t.InUse).Count}");
#endif

                    // 回収タイマー起動
                    if (tm.Enabled == false)
                    {
#if DEBUG
                        Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(GetThread): Unused thread collection event active");
#endif
                        tm.Start();
                    }

                }

                return thread;

            }

            /// <summary>
            /// 未使用スレッド回収タイマー発火
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private static void Tm_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
            {
                DisposeUnuseThread(10000);
            }

            /// <summary>
            /// スレッド返却
            /// </summary>
            /// <param name="thread"></param>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public static void ReturnThread(AesMaskThread thread)
            {
                lock (threadListLocker)
                {
                    // リストチェック
                    if (threadList.Find(t => t.Equals(thread)) == null) 
                        throw new ArgumentOutOfRangeException(nameof(thread));

                    // 使用中フラグOFF
                    thread.InUse = false;
                    // 最終使用タイムスタンプ更新
                    thread.LastUse = DateTimeOffset.UtcNow;
#if DEBUG
                    Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(ReturnThread): ManagedThreadId={thread.ManagedThreadId:x8}, Name={thread.Name}, Count={threadList.Count}, InUse={threadList.FindAll(t => t.InUse).Count}");
#endif
                }
            }

            /// <summary>
            /// スレッド削除
            /// </summary>
            /// <param name="thread"></param>
            /// <remarks>
            /// <seealso cref="AesMaskThread.CreateNextMask(object)"/>の最後のfinally句から呼ばれる
            /// </remarks>
            public static void RemoveThread(AesMaskThread thread)
            {
                lock (threadListLocker)
                {
                    threadList.Remove(thread);
#if DEBUG
                    Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(RemoveThread): ManagedThreadId={thread.ManagedThreadId:x8}, Name={thread.Name}, Count={threadList.Count}, InUse={threadList.FindAll(t => t.InUse).Count}");
#endif
                    // スレッド数が0なら回収イベントをSTOPする
                    if (threadList.Count == 0)
                    {
#if DEBUG
                        Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(RemoveThread): Unused thread collection event inactive");
#endif
                        tm.Stop();
                    }
                }
            }

            /// <summary>
            /// 未使用スレッド解放
            /// </summary>
            /// <param name="milliseconds"></param>
            private static void DisposeUnuseThread(long milliseconds)
            {
                // 未使用スレッドのリスト
                List<AesMaskThread> disposeList = new List<AesMaskThread>();
#if DEBUG
                int beforeCount;
                lock (threadListLocker)
                {
                    beforeCount = threadList.Count;
                }
#endif
                lock (threadListLocker)
                {
                    // 未使用スレッドのリストアップ
                    threadList.ForEach(thread =>
                    {
#if DEBUG
                        bool isCollection = false;
#endif
                        if (thread.IsUnUsed)
                        {
                            TimeSpan diff = DateTimeOffset.UtcNow - thread.LastUse;
                            if (milliseconds >= 0 && diff.TotalMilliseconds > milliseconds)
                            {
                                thread.InUse = true;
                                disposeList.Add(thread);
#if DEBUG
                                isCollection = true;
#endif
                            }
                        }
#if DEBUG
                        Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(DisposeUnuseThread): ManagedThreadId={thread.ManagedThreadId:x8}, Name={thread.Name}, Count={threadList.Count}, InUse={threadList.FindAll(t => t.InUse).Count}, LaseUse={thread.LastUse}, Collection={isCollection}");
#endif
                    });
                }
                
                // 未使用スレッド解放
                disposeList.ForEach(t => {
                    t.Dispose();
                });

#if DEBUG
                lock (threadListLocker)
                {
                    int afterCount = threadList.Count;
                    Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(DisposeUnuseThread): BeforeCount={beforeCount}, AfterCount={afterCount}");
               
                }
#endif

            }
        }

        #endregion

        #region "AesMaskThread"

        /// <summary>
        /// マスク配列作成処理クラス
        /// </summary>
        private class AesMaskThread : IDisposable
        {

            /// <summary>処理終了</summary>
            private bool isCompleted = true;

            /// <summary>エラー発生</summary>
            private bool isError = false;

            /// <summary>マスク配列</summary>
            private byte[] maskArray = null;

            /// <summary>暗号化用マスクワークセレクター</summary>
            private int maskSelecter = 0;

            /// <summary>暗号化用マスクカウンター</summary>
            private long maskCounter = 0;

            /// <summary>暗号化用マスク作成用配列</summary>
            private byte[] plainArray = new byte[MASK_SIZE];

            /// <summary>暗号化用マスクワーク1</summary>
            private byte[] maskArray0 = new byte[MASK_SIZE];

            /// <summary>暗号化用マスクワーク2</summary>
            private byte[] maskArray1 = new byte[MASK_SIZE];

            /// <summary>AES</summary>
            private static readonly System.Security.Cryptography.Aes aesCrypt = null;

            /// <summary>スレッド番号</summary>
            private static long threadNo = 0;

            /// <summary>スレッド番号ロック</summary>
            private static readonly object threadNoLocker = new object();

            /// <summary>AES暗号化/復号化</summary>
            private ICryptoTransform aesTrans = null;

            /// <summary>エラー情報</summary>
            private Exception exception = null;

            /// <summary>処理完了イベント</summary>
            private ManualResetEvent successEvent = new ManualResetEvent(false);

            /// <summary>実行待ちイベント</summary>
            private AutoResetEvent executeEvent = new AutoResetEvent(false);

            /// <summary>マスク作成タスクキャンセル用</summary>
            private readonly CancellationTokenSource cancelSource = new CancellationTokenSource();

            /// <summary>マスク作成タスクキャンセル用</summary>
            private readonly CancellationToken cancelToken;

            /// <summary>マスク作成スレッド</summary>
            private Thread createMaskThread = null;

            /// <summary>最後に使用されたタイムスタンプ</summary>
            public DateTimeOffset LastUse { get; set; } = DateTimeOffset.UtcNow;

            /// <summary>使用中フラグ</summary>
            public bool InUse { get; set; } = true;

            /// <summary>未使用判定</summary>
            public bool IsUnUsed
            {
                get
                {
                    return InUse == false &&
                           createMaskThread.IsAlive == true &&
                         ((createMaskThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == System.Threading.ThreadState.WaitSleepJoin) &&
                           executeEvent.WaitOne(0) == false;
                }
            }

            public int ManagedThreadId => createMaskThread.ManagedThreadId;

            public string Name => createMaskThread.Name;

            public System.Threading.ThreadState ThreadState=> createMaskThread.ThreadState;
            
#if DEBUG
            public double GetNextMaskTime { get; set; } = 0;
#endif

            /// <summary>
            /// コンストラクタ
            /// </summary>
            static AesMaskThread()
            {
                aesCrypt = System.Security.Cryptography.Aes.Create();
                aesCrypt.Mode = CipherMode.ECB;
                aesCrypt.BlockSize = 128;
            }

            /// <summary>
            /// コンストラクタ
            /// </summary>
            public AesMaskThread()
            {
                cancelToken = cancelSource.Token;
                createMaskThread = new Thread(CreateNextMask);
                lock (threadNoLocker) {
                    createMaskThread.Name = $"CreateNextMask_{threadNo++}";
                    if (threadNo == long.MaxValue) threadNo = 0;
                }
                createMaskThread.Priority = ThreadPriority.AboveNormal;
                createMaskThread.IsBackground = true;
                createMaskThread.Start(cancelToken);
            }

            /// <summary>
            /// AESキーをセットして内部状態をリセットする
            /// </summary>
            /// <param name="aesKey"></param>
            /// <param name="aesIv"></param>
            public void SetAesKey(byte[] aesKey, byte[] aesIv)
            {
                try { aesTrans?.Dispose(); } catch { } finally { aesTrans = null; }
                lock (aesCrypt)
                {
                    aesTrans = aesCrypt.CreateEncryptor(aesKey, aesIv);
                }
                maskSelecter = 0;
                maskCounter = 0;
#if DEBUG
                GetNextMaskTime = 0;
#endif
                // 初回の処理起動
                this.Start();
            }

            /// <summary>
            /// マスク作成処理起動
            /// </summary>
            private void Start()
            {
                isCompleted = false;
                successEvent.Reset();
                executeEvent.Set();
            }

            /// <summary>
            /// マスク配列取得
            /// </summary>
            /// <returns></returns>
            /// <exception cref="InvalidOperationException"></exception>
            public byte[] GetMaskArray()
            {
                // 完了を待機
                successEvent.WaitOne();

                // エラー発生時は例外をスロー
                if (isError)
                {
                    throw exception;
                }
                // 完了フラグチェック
                // Startの改善で不要になったはずだけど一応残しておく
                if (isCompleted != true)
                {
#if DEBUG
                    Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(GetMaskArray): isCompleted={isCompleted}, isError={isError}, ThreadState={createMaskThread.ThreadState}");
#endif
                    throw new InvalidOperationException("Invalid status of the AES mask array creation thread.");
                }

                byte[] returnArray = maskArray;

                Start();

                // マスク配列を返却
                return returnArray;
            }

            /// <summary>
            /// 次のマスク値を作成
            /// </summary>
            /// <param name="obj"></param>
            private unsafe void CreateNextMask(object obj)
            {
                CancellationToken token = (CancellationToken)obj;
                try
                {
                    while (true)
                    {
                        // 開始を待機
                        try
                        {
                            // キャンセルリクエスト判定
                            if (token.IsCancellationRequested)
                            {
#if DEBUG
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(CreateNextMask): CancelRequest(1)");
#endif
                                return;
                            }
                            // ExecuteイベントがSetされるまで待機する
                            executeEvent.WaitOne(Timeout.Infinite);
                            // キャンセルリクエスト判定
                            if (token.IsCancellationRequested)
                            {
#if DEBUG
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(CreateNextMask): CancelRequest(2)");
#endif
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            exception = ex;
                            return;
                        }

                        try
                        {
#if DEBUG
                            Stopwatch stp = Stopwatch.StartNew();
#endif

                            // 使用する配列取得
                            maskArray = ((maskSelecter++ % 2 == 0) ? maskArray0 : maskArray1);

                            // マスク作成用のPlainArrayを作成
                            fixed (byte* pa = plainArray)
                            {
                                long* lp = (long*)pa;
                                for (int i = 0; i < MASK_SIZE; i += 16)
                                {
                                    *lp = ++maskCounter;
                                    lp += 2;
                                }
                            }

                            // キャンセルリクエスト判定
                            if (token.IsCancellationRequested)
                            {
#if DEBUG
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(CreateNextMask): CancelRequest(3)");
#endif
                                return;
                            }

                            // マスク配列作成
                            aesTrans.TransformBlock(plainArray, 0, MASK_SIZE, maskArray, 0);

                            // 完了フラグ設定
                            isCompleted = true;

#if DEBUG
                            GetNextMaskTime += stp.Elapsed.TotalMilliseconds;
#endif

                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(CreateNextMask): Catch Exception={ex}");
#endif
                            isError = true;
                            exception = ex;
                            return;
                        }
                        finally
                        {
                            // nullは発生しないはず
                            try { successEvent?.Set(); } catch { }
                        }
                    }
                }
                finally
                {
                    AesMaskThreadPool.RemoveThread(this);
                }
            }

            #region Dispose

            private bool disposedValue = false;

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    disposedValue = true;

                    if (disposing)
                    {
#if DEBUG
                        Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(Dispose).ThreadState={createMaskThread?.ThreadState}, Priority={createMaskThread.Priority}, IsAlive={createMaskThread?.IsAlive}, IsThreadPoolThread={createMaskThread.IsThreadPoolThread}");
#endif

                        // CreateMaskThredのお掃除
                        // ThreadState毎の停止処理をやめて、キャンセル=>executeEventのセットのみでループを抜けられる用に変更
                        if (createMaskThread != null && createMaskThread.IsAlive)
                        {

                            // CancelRequest
                            try { cancelSource.Cancel(); }
#if DEBUG
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(Dispose).cancelSource.Cancel(): {ex}");
                            }
#else
                            catch { }
#endif
                            // ExecuteEvent Set
                            try { executeEvent.Set(); }
#if DEBUG
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(Dispose).ExecuteEvent.Set(): {ex}");
                            }
#else
                            catch { }
#endif
                            // Threadの終了を待機
                            try
                            {
                                createMaskThread.Join(Timeout.Infinite);
                            }
#if DEBUG
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(Dispose).Join: {ex}");
                            }
#else
                            catch { }
#endif
                            createMaskThread = null;
                        }

                        try { aesTrans?.Dispose(); } catch { } finally { aesTrans = null; }
                        try { successEvent?.Dispose(); } catch { } finally { successEvent = null; }
                        try { executeEvent?.Dispose(); } catch { } finally { executeEvent = null; }
                        try { cancelSource?.Dispose(); } catch { }
                        createMaskThread = null;
                    }

                    // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                    // TODO: 大きなフィールドを null に設定します
                    maskArray = null;
                    maskArray0 = null;
                    maskArray1 = null;
                    plainArray = null;

                }
            }

            // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
            // ~AesMaskCreateThreadInfo()
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

#endregion

        #region "Private"

        /// <summary>
        /// キー初期化
        /// </summary>
        /// <param name="aesMode">AESモード</param>
        /// <param name="password">パスワード</param>
        /// <param name="aesSalt"></param>
        /// <remarks></remarks>
        private byte[] InitKey(ZipArcClass.ENCRYPTION_OPTION aesMode, byte[] password, byte[] aesSalt)
        {
            // PBKDF2(暗号化キー、HMACキー、パスワード検証値を取得)
            byte[] pbkdf2Output;
#if NET472_OR_GREATER || NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(password, aesSalt, 1000, HashAlgorithmName.SHA1))
#else
            using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(password, aesSalt, 1000))
#endif
            {
                pbkdf2Output = pbkdf2.GetBytes((AES_KEY_LENGTH[(int)aesMode] * 2) + 2);
            }

            // AES Key
            int i = 0;
            byte[] aesKey = new byte[AES_KEY_LENGTH[(int)aesMode]];
            Array.Copy(pbkdf2Output, i, aesKey, 0, aesKey.Length);
            i += aesKey.Length;

            // HMAC Key
            byte[] hmacKey = new byte[AES_KEY_LENGTH[(int)aesMode]];
            Array.Copy(pbkdf2Output, i, hmacKey, 0, hmacKey.Length);
            i += hmacKey.Length;

            // Password Validation Data
            byte[] passwordValidationData = new byte[2];
            Array.Copy(pbkdf2Output, i, passwordValidationData, 0, passwordValidationData.Length);

            // IVはall zero
            byte[] aesIv = new byte[16];

            // AES Encryptor作成
            aesMaskThread.SetAesKey(aesKey, aesIv);

            // HMACSHA1
            calcHmac = new HMACSHA1(hmacKey);
            if (streamMode == StreamMode.ENCRYPT)
            {
                writeStream.Write(aesSalt, 0, aesSalt.Length);
                writeStream.Write(passwordValidationData, 0, passwordValidationData.Length);
            }
            return passwordValidationData;
        }

        /// <summary>
        /// 次のマスク値を取得
        /// </summary>
        /// <remarks></remarks>
        private void GetNextMask()
        {
#if DEBUG
            Stopwatch stp = Stopwatch.StartNew();
#endif

            // マスク配列設定
            aesMaskBytes = aesMaskThread.GetMaskArray();

#if DEBUG
            getNextMaskWaitTime += stp.Elapsed.TotalMilliseconds;
#endif

            aesMaskPosition = 0;

        }

        /// <summary>
        /// 入出力バッファとマスク配列をxorして暗号化／復号化を行う
        /// </summary>
        /// <param name="array">入出力バッファ</param>
        /// <param name="offset">オフセット</param>
        /// <param name="count">カウント</param>
        private unsafe void MaskByteArray(byte[] array, int offset, int count)
        {
            int xorCount = 0;
            int iCount = 0;
            int iMaskSize = 0;
#if NET7_0_OR_GREATER
            UIntPtr iPtr;
#endif
            fixed (byte* ap = array)
            {
                long* alp = (long*)(ap + offset);

                while (xorCount < count)
                {
                    // 次のマスク配列取得
                    if (aesMaskPosition == MASK_SIZE)
                    {
                        GetNextMask();
                    }

                    fixed (byte* mp = aesMaskBytes)
                    {
                        long* mlp = (long*)(mp + aesMaskPosition);

#if NET7_0_OR_GREATER
                        if (vector512Supported)
                        {
                            iCount = count - 64;
                            iMaskSize = MASK_SIZE - 64;
                            for (iPtr = 0; xorCount <= iCount && aesMaskPosition <= iMaskSize; xorCount += 64, aesMaskPosition += 64, iPtr += 8)
                            {
                                Vector512.StoreUnsafe(Vector512.Xor(Vector512.LoadUnsafe(ref *alp, iPtr), Vector512.LoadUnsafe(ref *mlp, iPtr)), ref *alp, iPtr);
                            }
                            alp += iPtr;
                            mlp += iPtr;
                        }
                        else if (vector256Supported)
                        {
                            iCount = count - 32;
                            iMaskSize = MASK_SIZE - 32;
                            for (iPtr = 0; xorCount <= iCount && aesMaskPosition <= iMaskSize; xorCount += 32, aesMaskPosition += 32, iPtr += 4)
                            {
                                Vector256.StoreUnsafe(Vector256.Xor(Vector256.LoadUnsafe(ref *alp, iPtr), Vector256.LoadUnsafe(ref *mlp, iPtr)), ref *alp, iPtr);
                            }
                            alp += iPtr;
                            mlp += iPtr;
                        }
                        else if (vector128Supported)
                        {
                            iCount = count - 16;
                            iMaskSize = MASK_SIZE - 16;
                            for (iPtr = 0; xorCount <= iCount && aesMaskPosition <= iMaskSize; xorCount += 16, aesMaskPosition += 16, iPtr += 2)
                            {
                                Vector128.StoreUnsafe(Vector128.Xor(Vector128.LoadUnsafe(ref *alp, iPtr), Vector128.LoadUnsafe(ref *mlp, iPtr)), ref *alp, iPtr);
                            }
                            alp += iPtr;
                            mlp += iPtr;
                        }
#elif NET5_0_OR_GREATER
                        if (avx2Supported)
                        {
                            // AVX2 を使用して32Byte単位にxor
                            iCount = count - 32;
                            iMaskSize = MASK_SIZE - 32;
                            for (; xorCount <= iCount && aesMaskPosition <= iMaskSize; xorCount += 32, aesMaskPosition += 32, alp += 4, mlp += 4)
                            {
                                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                            }
                        }
                        else if (advSimdSupported)
                        {
                            //  AdvSIMD を使用して16Byte単位にxor
                            iCount = count - 16;
                            iMaskSize = MASK_SIZE - 16;
                            for (; xorCount <= iCount && aesMaskPosition <= iMaskSize; xorCount += 16, aesMaskPosition += 16, alp += 2, mlp += 2)
                            {
                                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                            }
                        }
#endif

                        // 8byte単位にxor
                        iCount = count - 8;
                        iMaskSize = MASK_SIZE - 8;
                        for (; xorCount <= iCount && aesMaskPosition <= iMaskSize; xorCount += 8, aesMaskPosition += 8)
                        {
                            *alp++ ^= *mlp++;
                        }

                        // 1byte単位にxor
                        byte* abp = (byte*)alp;
                        byte* mbp = (byte*)mlp;
                        for (; xorCount < count && (aesMaskPosition) < MASK_SIZE; xorCount++, aesMaskPosition++)
                        {
                            *abp = (byte)(*abp++ ^ *mbp++);
                        }
                        alp = (long*)abp;
                    }
                }
            }
        }

        /// <summary>
        /// Hashチェック
        /// </summary>
        /// <exception cref="CryptographicException"></exception>
        private void CheckHash()
        {
            // 検証
            byte[] hash = new byte[10];
            calcHmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            Array.Copy(calcHmac.Hash, hash, hash.Length);
            byte[] varidationData = new byte[10];
            try
            {
                ShareMethodClass.StreamReadBuffer(this.readStream.BaseStream, varidationData);
            }
            catch (EndOfStreamException)
            {
                throw new EndOfStreamException("AES encryption stream is broken.");
            }
            catch { throw; }
#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(CheckHash): Length={totalReadCount}, varidationValue={ShareMethodClass.ByteArrayToHex(varidationData, true)}, hash={ShareMethodClass.ByteArrayToHex(hash, true)}");
#endif
            if (!ShareMethodClass.ByteArrayCompare(varidationData, hash))
            {
                throw new CryptographicException();
            }
        }

        #endregion

        #region "Override"

        public override bool CanRead => streamMode == StreamMode.DECRYPT;

        public override bool CanSeek => false;

        public override bool CanWrite => streamMode == StreamMode.ENCRYPT;

        public override long Length
        {
            get
            {
                if (streamMode == StreamMode.ENCRYPT)
                {
                    return writeStream.Length;
                }
                return readStream.Length;
            }
        }

        public override long Position
        {
            get
            {
                if (streamMode == StreamMode.ENCRYPT)
                {
                    return writeStream.Position;
                }
                return readStream.Position;
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        public override void Flush()
        {
            if (this.streamMode == StreamMode.DECRYPT)
            {
                throw new NotSupportedException();
            }

            // 検証値書き込み
            this.calcHmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            byte[] authCode = this.calcHmac.Hash;
            this.writeStream.Write(authCode, 0, 10);
            this.writeStream.Flush();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }


        public override void Write(byte[] buffer, int offset, int count)
        {
#if DEBUG
            readWriteStp.Restart();
            readWriteCount++;
#endif
            if (streamMode == StreamMode.DECRYPT)
            {
                throw new NotSupportedException();
            }

            // バッファをマスク
            MaskByteArray(buffer, offset, count);

            // バッファを出力
            writeStream.Write(buffer, offset, count);

            // ハッシュ算出
            calcHmac.TransformBlock(buffer, offset, count, null, 0);
#if DEBUG
            readWriteTime += readWriteStp.Elapsed.TotalMilliseconds;
#endif
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
#if DEBUG
            readWriteStp.Restart();
            readWriteCount++;
#endif
            if (streamMode == StreamMode.ENCRYPT)
            {
                throw new NotSupportedException();
            }

            // ストリーム読み込み
            int readCount = readStream.Read(buffer, offset, count);
            if (readCount == 0)
            {
                if(totalReadCount != readStream.Length)
                {
                    throw new EndOfStreamException("AES encryption stream is broken.");
                }
                return readCount;
            }

            // ハッシュ算出
            this.calcHmac.TransformBlock(buffer, offset, readCount, null, 0);

            // バッファをマスク
            MaskByteArray(buffer, offset, readCount);

            totalReadCount += readCount;

            // 最後まで読んだときの処理
            if (totalReadCount == readStream.Length)
            {
                CheckHash();
            }

#if DEBUG
            readWriteTime += readWriteStp.Elapsed.TotalMilliseconds;
#endif
            return readCount;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        private bool disposedValue = false;

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
#if DEBUG
                    Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream: ConstructorElaps={constructorExecTime}ms, ReadWriteCount={readWriteCount}, ReadWriteElaps={readWriteTime}ms, GetNextMaskTotalTime={aesMaskThread.GetNextMaskTime}ms, GetNextMaskTotalWaittime={getNextMaskWaitTime}ms");
#endif
                    if (aesMaskThread != null)
                    {
                        //try { aesMaskThread.Dispose(); } catch (Exception) { }
                        AesMaskThreadPool.ReturnThread(aesMaskThread);
                    }
                    try { calcHmac?.Dispose(); } catch { } finally { calcHmac = null; }
                    writeStream = null;
                    try { readStream?.Dispose(); } catch { } finally { }
                }
                base.Dispose(disposing);
            }
            disposedValue = true;
        }

        #endregion

    }
}
