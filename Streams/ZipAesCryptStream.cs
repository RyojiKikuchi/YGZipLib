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
using YGZipLib.Properties;
#elif YGMAILLIB
using YGMailLib.Zip.Common;
using YGMailLib.Zip.Streams;
using YGMailLib.Zip.Properties;
#endif
#if NET5_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Serialization.Formatters;
using System.Runtime.CompilerServices;
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
        private readonly AesMaskGenerator aesMaskGenerator = null;

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
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream: Constructor length={length}, vector512Supported={Vector512.IsHardwareAccelerated}, vector256Supported={Vector256.IsHardwareAccelerated}, vector128Supported={Vector128.IsHardwareAccelerated}");
#elif NET5_0_OR_GREATER
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream: Constructor length={length}, avx2Supported={Avx2.IsSupported}, advSimdSupported={AdvSimd.IsSupported}");
#else
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream: Constructor length={length}");
#endif
#endif

            // マスク配列作成スレッド取得
            aesMaskGenerator = AesMaskThreadPool.GetGenerator();

            try
            {

                // マスク配列ポインタを最後に設定。
                // 初回処理時にGetNextMask()を呼び出すことで、マスク配列を取得する
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
                        throw new ArgumentException(Resources.ERRMSG_INVALID_AESMODE, nameof(aesMode));
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
                    byte[] passwordValidationDataInit = InitKey(aesMode, password, aesSalt);

                    // saltとパスワード検証値をストリームに書き込む
                    writeStream.Write(aesSalt, 0, aesSalt.Length);
                    writeStream.Write(passwordValidationDataInit, 0, passwordValidationDataInit.Length);

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
                        throw new CryptographicException(Resources.ERRMSG_INCORRECT_PASSWORD);
                    }

                    // ストリーム設定
                    readStream = new InputStream(baseInputStream, baseInputStream.Length - baseInputStream.Position - 10);
                }

            }
            catch (Exception)
            {
                aesMaskGenerator?.Dispose();
                aesMaskGenerator = null;
                throw;
            }

            // マスク配列処理メソッド設定
            SetMaskByteArrayXorMethod();

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

            /// <summary>未使用スレッド判定閾値(ms)</summary>
            private const double UNUSE_THREAD_DISPOSE_THRESHOLD = 10000;

            /// <summary>マスク配列作成処理格納リスト</summary>
            private static readonly List<AesMaskGenerator> threadList = new List<AesMaskGenerator>();

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
            public static AesMaskGenerator GetGenerator()
            {

                return ThreadListAccess(ListAccessType.GET, null);

            }

            /// <summary>
            /// 未使用スレッド回収タイマー発火
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private static void Tm_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
            {
                DisposeUnuseGenerator();
            }

            /// <summary>
            /// マスク生成スレッド返却
            /// </summary>
            /// <param name="generator"></param>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public static void ReturnGenerator(AesMaskGenerator generator)
            {

                ThreadListAccess(ListAccessType.RETURN, generator);
            
            }

            /// <summary>
            /// マスク生成返却
            /// </summary>
            /// <param name="generator"></param>
            /// <remarks>
            /// <seealso cref="AesMaskGenerator.CreateNextMask(object)"/>の最後のfinally句から呼ばれる
            /// </remarks>
            public static void RemoveGenerator(AesMaskGenerator generator)
            {

                   ThreadListAccess(ListAccessType.REMOVE, generator);

            }

            /// <summary>
            /// 未使用スレッド解放
            /// </summary>
            private static void DisposeUnuseGenerator()
            {
                // 未使用スレッドのリスト
                List<AesMaskGenerator> disposeList = new List<AesMaskGenerator>();
#if DEBUG
                int beforeCount;
                lock (threadListLocker)
                {
                    beforeCount = threadList.Count;
                }
#endif

                ThreadListAccess(ListAccessType.DISPOSEUNUSETHREAD, null);

#if DEBUG
                lock (threadListLocker)
                {
                    int afterCount = threadList.Count;
                    Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(DisposeUnuseThread): BeforeCount={beforeCount}, AfterCount={afterCount}");

                }
#endif

            }
            
            private enum ListAccessType
            {
                GET,
                RETURN,
                REMOVE,
                DISPOSEUNUSETHREAD
            }

            private static AesMaskGenerator ThreadListAccess(ListAccessType accessType, AesMaskGenerator returnGenerator)
            {

                List<AesMaskGenerator> disposeList = null;

                lock (threadListLocker)
                {
                    switch (accessType)
                    {
                        case ListAccessType.GET:

                            // 未使用スレッド取得
                            AesMaskGenerator generator = threadList.Find(t => t.IsUnUsed == true);

                            // 未使用スレッドが存在しない場合新規スレッドを作成
                            if (generator == null)
                            {
                                generator = new AesMaskGenerator();
                                threadList.Add(generator);
                            }

                            // 使用中フラグ設定
                            generator.InUse = true;

#if DEBUG
                            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(GetThread): ManagedThreadId={generator.ManagedThreadId:x8}, Name={generator.Name}, Count={threadList.Count}, InUse={threadList.FindAll(t => t.InUse).Count}");
#endif

                            // 回収タイマー起動
                            if (tm.Enabled == false)
                            {
#if DEBUG
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(GetThread): Unused thread collection event active");
#endif
                                tm.Start();
                            }

                            return generator;

                        case ListAccessType.RETURN:

                            // リストチェック
                            if (threadList.Find(t => t.Equals(returnGenerator)) == null)
                                throw new ArgumentOutOfRangeException(nameof(returnGenerator));

                            // 使用中フラグOFF
                            returnGenerator.InUse = false;
                            // 最終使用タイムスタンプ更新
                            returnGenerator.UnUseReset();

#if DEBUG
                            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(ReturnThread): ManagedThreadId={returnGenerator.ManagedThreadId:x8}, Name={returnGenerator.Name}, Count={threadList.Count}, InUse={threadList.FindAll(t => t.InUse).Count}");
#endif
                            
                            return null;

                        case ListAccessType.REMOVE:

                            threadList.Remove(returnGenerator);
#if DEBUG
                            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(RemoveThread): ManagedThreadId={returnGenerator.ManagedThreadId:x8}, Name={returnGenerator.Name}, Count={threadList.Count}, InUse={threadList.FindAll(t => t.InUse).Count}");
#endif
                            // スレッド数が0なら回収イベントをSTOPする
                            if (threadList.Count == 0)
                            {
#if DEBUG
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(RemoveThread): Unused thread collection event inactive");
#endif
                                tm.Stop();
                            }

                            return null;

                        case ListAccessType.DISPOSEUNUSETHREAD:

                            disposeList = new List<AesMaskGenerator>();

                            // 未使用スレッドのリストアップ
                            threadList.ForEach(t =>
                            {
#if DEBUG
                                bool isCollection = false;
#endif
                                if (t.IsUnUsed)
                                {
                                    if (t.UnUseMilliseconds > UNUSE_THREAD_DISPOSE_THRESHOLD)
                                    {
                                        t.InUse = true;
                                        disposeList.Add(t);
#if DEBUG
                                        isCollection = true;
#endif
                                    }
                                }
#if DEBUG
                                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(DisposeUnuseThread): ManagedThreadId={t.ManagedThreadId:x8}, Name={t.Name}, Count={threadList.Count}, InUse={threadList.FindAll(t2 => t2.InUse).Count}, UnUseMilliseconds={t.UnUseMilliseconds}, Collection={isCollection}");
#endif
                            });
                            
                            break;

                        default:
                            throw new ArgumentOutOfRangeException(nameof(accessType));
                    }

                }

                // ロック外でDisposeを実行
                if (accessType == ListAccessType.DISPOSEUNUSETHREAD && disposeList != null)
                {
                    disposeList.ForEach(t => t.Dispose());
                    disposeList.Clear();
                    disposeList = null;
                }

                return null;

            }


        }

        #endregion

        #region "AesMaskGenerator"

        /// <summary>
        /// マスク配列作成
        /// </summary>
        private class AesMaskGenerator : IDisposable
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

            /// <summary>cancel済</summary>
            private bool canceled = false;

            private long unUseResetTicks { get; set; } = Environment.TickCount;

            /// <summary>未使用経過時間(ms)</summary>
            public long UnUseMilliseconds
            {
                get
                {
                    long nowTicks = Environment.TickCount;
                    if (nowTicks > unUseResetTicks)
                    {
                        return nowTicks - unUseResetTicks;
                    }
                    else
                    {
                        return 0;
                    }
                }
            }

            /// <summary>使用中フラグ</summary>
            public bool InUse { get; set; } = true;

            /// <summary>未使用判定</summary>
            public bool IsUnUsed
            {
                get
                {
                    return canceled == false && InUse == false &&
                           createMaskThread.IsAlive == true &&
                         ((createMaskThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == System.Threading.ThreadState.WaitSleepJoin) &&
                           executeEvent.WaitOne(0) == false;
                }
            }

            public int ManagedThreadId => createMaskThread.ManagedThreadId;

            public string Name => createMaskThread.Name;

            public System.Threading.ThreadState ThreadState => createMaskThread.ThreadState;

#if DEBUG
            public double GetNextMaskTime { get; set; } = 0;
#endif

            /// <summary>
            /// コンストラクタ
            /// </summary>
            static AesMaskGenerator()
            {
                aesCrypt = System.Security.Cryptography.Aes.Create();
                aesCrypt.Mode = CipherMode.ECB;
                aesCrypt.BlockSize = 128;
            }

            /// <summary>
            /// コンストラクタ
            /// </summary>
            public AesMaskGenerator()
            {
                cancelToken = cancelSource.Token;
                createMaskThread = new Thread(CreateNextMask);
                lock (threadNoLocker) {
                    createMaskThread.Name = $"CreateNextMask_{Interlocked.Increment(ref threadNo)}";
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
            /// 未使用タイマーリセット
            /// </summary>
            public void UnUseReset()
            {
                unUseResetTicks = Environment.TickCount;
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
                    throw new InvalidOperationException(Resources.ERRMSG_INTERNAL_AESMASKGEN_ERROR);
                }

                byte[] returnArray = maskArray;

                // 次のマスク作成処理を起動
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

                            this.UnUseReset();

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
                    AesMaskThreadPool.RemoveGenerator(this);
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

                        // キャンセル済みにセット
                        canceled = true;

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

            // PBKDF2(AES暗号化キー、HMACキー、パスワード検証値を取得)
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

            // AES Encryptor作成、IVはall zero
            aesMaskGenerator.SetAesKey(aesKey, new byte[16]);

            // HMAC Key
            byte[] hmacKey = new byte[AES_KEY_LENGTH[(int)aesMode]];
            Array.Copy(pbkdf2Output, i, hmacKey, 0, hmacKey.Length);
            i += hmacKey.Length;

            // Password Validation Data
            byte[] passwordValidationData = new byte[2];
            Array.Copy(pbkdf2Output, i, passwordValidationData, 0, passwordValidationData.Length);

            // HMACSHA1
            calcHmac = new HMACSHA1(hmacKey);

            return passwordValidationData;

        }

        /// <summary>
        /// 次のマスク値を取得
        /// </summary>
        /// <remarks></remarks>
        private void GetNextMask()
        {

            // マスク配列設定
            aesMaskBytes = aesMaskGenerator.GetMaskArray();

            // マスク配列ポインタをリセット
            aesMaskPosition = 0;

        }

        #region "MaskByteArray"

        /// <summary>
        /// AESマスク処理デリゲート
        /// </summary>
        /// <param name="alp">マスク対象配列アクセス用ポインタ</param>
        /// <param name="mlp">マスク用配列アクセス用ポインタ</param>
        /// <param name="count">マスクバイト数</param>
        /// <param name="xorCount">xor処理済カウンタ</param>
        private unsafe delegate void MaskByteArrayXorMethodDelegate(ref long* alp, ref long* mlp, int count, ref int xorCount);

        private unsafe MaskByteArrayXorMethodDelegate maskByteArrayXorMethod = null;

        /// <summary>
        /// マスク配列処理メソッド設定
        /// </summary>
        private unsafe void SetMaskByteArrayXorMethod()
        {

            // SIMDサポート判定

#if NET8_0_OR_GREATER

            if (Vector512.IsHardwareAccelerated)
                maskByteArrayXorMethod = MaskByteArrayXorVector512;
            else if (Vector256.IsHardwareAccelerated)
                maskByteArrayXorMethod = MaskByteArrayXorVector256;
            else if (Vector128.IsHardwareAccelerated)
                maskByteArrayXorMethod = MaskByteArrayXorVector128;
            else
                maskByteArrayXorMethod = MaskByteArrayXorNoSimd;

#elif NET5_0_OR_GREATER

            if (Avx2.IsSupported)
                maskByteArrayXorMethod = MaskByteArrayXorAvx2;
            else if (AdvSimd.IsSupported)
                maskByteArrayXorMethod = MaskByteArrayXorAdvSimd;
            else
                maskByteArrayXorMethod = MaskByteArrayXorNoSimd;

#else

            maskByteArrayXorMethod = MaskByteArrayXorNoSimd;

#endif

        }

#if NET7_0_OR_GREATER


        /// <summary>Vector512(64Byte)でマスク処理</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void MaskByteArrayXorVector512(ref long* alp, ref long* mlp, int count, ref int xorCount)
        {

            // 256バイト単位で処理
            for (; xorCount <= (count - 256) && aesMaskPosition <= (MASK_SIZE - 256); xorCount += 256, aesMaskPosition += 256)
            {
                Vector512.Store(Vector512.Xor(Vector512.Load(alp), Vector512.Load(mlp)), alp);
                alp += 8; mlp += 8;
                Vector512.Store(Vector512.Xor(Vector512.Load(alp), Vector512.Load(mlp)), alp);
                alp += 8; mlp += 8;
                Vector512.Store(Vector512.Xor(Vector512.Load(alp), Vector512.Load(mlp)), alp);
                alp += 8; mlp += 8;
                Vector512.Store(Vector512.Xor(Vector512.Load(alp), Vector512.Load(mlp)), alp);
                alp += 8; mlp += 8;
            }

            // 64バイト単位で処理
            for (; xorCount <= (count - 64) && aesMaskPosition <= (MASK_SIZE - 64); xorCount += 64, aesMaskPosition += 64)
            {
                Vector512.Store(Vector512.Xor(Vector512.Load(alp), Vector512.Load(mlp)), alp);
                alp += 8; mlp += 8;
            }

        }

        /// <summary>Vector256(32Byte)でマスク処理</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void MaskByteArrayXorVector256(ref long* alp, ref long* mlp, int count, ref int xorCount)
        {

            // 256バイト単位で処理
            for (; xorCount <= (count - 256) && aesMaskPosition <= (MASK_SIZE - 256); xorCount += 256, aesMaskPosition += 256)
            {
                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;
                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;

                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;
                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;

                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;
                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;

                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;
                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;
            }

            // 64バイト単位で処理
            for (; xorCount <= (count - 64) && aesMaskPosition <= (MASK_SIZE - 64); xorCount += 64, aesMaskPosition += 64)
            {
                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;
                Vector256.Store(Vector256.Xor(Vector256.Load(alp), Vector256.Load(mlp)), alp);
                alp += 4; mlp += 4;
            }

        }

        /// <summary>Vector128でマスク処理</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void MaskByteArrayXorVector128(ref long* alp, ref long* mlp, int count, ref int xorCount)
        {

            // 256バイト単位で処理
            for (; xorCount <= (count - 256) && aesMaskPosition <= (MASK_SIZE - 256); xorCount += 256, aesMaskPosition += 256)
            {
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;

                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;

                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;

                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
            }

            // 64バイト単位で処理
            for (; xorCount <= (count - 64) && aesMaskPosition <= (MASK_SIZE - 64); xorCount += 64, aesMaskPosition += 64)
            {
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
                Vector128.Store(Vector128.Xor(Vector128.Load(alp), Vector128.Load(mlp)), alp);
                alp += 2; mlp += 2;
            }

        }

#endif
#if NET5_0_OR_GREATER

        /// <summary>Avx2でマスク処理</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void MaskByteArrayXorAvx2(ref long* alp, ref long* mlp, int count, ref int xorCount)
        {
            // 256バイト単位で処理
            for (; xorCount <= (count - 256) && aesMaskPosition <= (MASK_SIZE - 256); xorCount += 256, aesMaskPosition += 256)
            {
                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;
                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;

                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;
                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;

                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;
                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;

                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;
                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;
            }

            // 64バイト単位で処理
            for (; xorCount <= (count - 64) && aesMaskPosition <= (MASK_SIZE - 64); xorCount += 64, aesMaskPosition += 64)
            {
                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;
                Avx2.Store(alp, Avx2.Xor(Avx2.LoadVector256(alp), Avx2.LoadVector256(mlp)));
                alp += 4; mlp += 4;
            }

        }

        /// <summary>Avx2でマスク処理</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void MaskByteArrayXorAdvSimd(ref long* alp, ref long* mlp, int count, ref int xorCount)
        {

            // 256バイト単位で処理
            for (; xorCount <= (count - 256) && aesMaskPosition <= (MASK_SIZE - 256); xorCount += 256, aesMaskPosition += 256)
            {
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;

                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;

                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;

                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
            }

            // 64バイト単位で処理
            for (; xorCount <= (count - 64) && aesMaskPosition <= (MASK_SIZE - 64); xorCount += 64, aesMaskPosition += 64)
            {
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
                AdvSimd.Store(alp, AdvSimd.Xor(AdvSimd.LoadVector128(alp), AdvSimd.LoadVector128(mlp)));
                alp += 2; mlp += 2;
            }

        }

#endif

        /// <summary>Longでマスク処理</summary>
#if NET5_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private unsafe void MaskByteArrayXorNoSimd(ref long* alp, ref long* mlp, int count, ref int xorCount)
        {

            // 256バイト単位にxor
            for (; xorCount <= (count - 256) && aesMaskPosition <= (MASK_SIZE - 256); xorCount += 256, aesMaskPosition += 256)
            {
                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;
                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;

                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;
                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;

                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;
                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;

                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;
                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;
            }

            // 64バイト単位にxor
            for (; xorCount <= (count - 64) && aesMaskPosition <= (MASK_SIZE - 64); xorCount += 64, aesMaskPosition += 64)
            {
                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;
                *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++; *alp++ ^= *mlp++;
            }
        }

        /// <summary>バイト配列にマスク処理</summary>
        private unsafe void MaskByteArray(byte[] array, int offset, int count)
        {
            int xorCount = 0;
         
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

                        // 256/64byte単位にxor
                        maskByteArrayXorMethod(ref alp, ref mlp, count, ref xorCount);

                        // 8byte単位にxor
                        for (; xorCount <= (count - 8) && aesMaskPosition <= (MASK_SIZE - 8); xorCount += 8, aesMaskPosition += 8)
                        {
                            *alp++ ^= *mlp++;
                        }

                        // 1byte単位にxor
                        byte* abp = (byte*)alp;
                        byte* mbp = (byte*)mlp;
                        for (; xorCount < count && aesMaskPosition < MASK_SIZE; xorCount++, aesMaskPosition++)
                        {
                            *abp = (byte)(*abp++ ^ *mbp++);
                        }
                        alp = (long*)abp;

                    }
                }
            }
        }

        #endregion

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
            ShareMethodClass.StreamReadBuffer(this.readStream.BaseStream, varidationData);
#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream(CheckHash): Length={totalReadCount}, varidationValue={ShareMethodClass.ByteArrayToHex(varidationData, true)}, hash={ShareMethodClass.ByteArrayToHex(hash, true)}");
#endif
            if (!ShareMethodClass.ByteArrayCompare(varidationData, hash))
            {
                throw new CryptographicException(Resources.ERRMSG_AES_STREAM_BROKEN);
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
                    throw new EndOfStreamException(Resources.ERRMSG_AES_STREAM_BROKEN);
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
                    Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : ZipAesCryptStream: ConstructorElaps={constructorExecTime}ms, ReadWriteCount={readWriteCount}, ReadWriteElaps={readWriteTime}ms, GetNextMaskTotalTime={aesMaskGenerator.GetNextMaskTime}ms, GetNextMaskTotalWaittime={getNextMaskWaitTime}ms");
#endif
                    if (aesMaskGenerator != null)
                    {
                        //try { aesMaskGenerator.Dispose(); } catch (Exception) { }
                        AesMaskThreadPool.ReturnGenerator(aesMaskGenerator);
                    }
                    try { calcHmac?.Dispose(); } catch { } finally { calcHmac = null; }
                    writeStream = null;
                    try { readStream?.Dispose(); } catch { } finally { }
                }
                maskByteArrayXorMethod = null;
                base.Dispose(disposing);
            }
            disposedValue = true;
        }

        #endregion

    }
}
