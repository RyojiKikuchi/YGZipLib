using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
#if DEBUG
using System.Diagnostics;
#endif
#if YGZIPLIB
using YGZipLib.Common;
using YGZipLib.Properties;
using YGZipLib.Streams;
#elif YGMAILLIB
using YGMailLib.Zip.Common;
using YGMailLib.Zip.Streams;
using YGMailLib.Zip.Properties;
#endif


#if YGZIPLIB
namespace YGZipLib
#elif YGMAILLIB
namespace YGMailLib.Zip
#endif
{

    /// <summary>ZIP書庫作成クラス</summary>
#if YGZIPLIB
    public class ZipArcClass : IDisposable
#else
    internal class ZipArcClass : IDisposable
#endif
    {


        #region "ENUM"

        /// <summary>
        /// 格納するタイムスタンプ
        /// </summary>
        public enum StoreTimestampOption : UInt16
        {
            /// <summary></summary>
            DosOnly = 0x0000,
            /// <summary>NTFS Timestamp(default)</summary>
            Ntfs = 0x000a,
            /// <summary>extended timestamp</summary>
            ExtendedTimestamp = 0x5455,
            /// <summary>NTFS Timestampとextended timestampの両方</summary>
            NtfsAndExtendedTimestamp = 0x0001
        }

        /// <summary>
        /// 圧縮オプション
        /// </summary>
        /// <remarks></remarks>
        public enum COMPRESSION_OPTION : int
        {
            /// <summary>圧縮なし</summary>
            NOT_COMPRESSED = 1,
            /// <summary>Deflate圧縮</summary>
            DEFLATE = 2
        }

        /// <summary>
        /// 暗号化方式
        /// </summary>
        /// <remarks>
        /// <see cref="ZipArcClass.Password"/>が指定された場合に使用する暗号化方式を指定します。
        /// </remarks>
        public enum ENCRYPTION_OPTION : int
        {
            /// <summary>ZIP2.0(Traditional PKWARE Encryption)</summary>
            TRADITIONAL = 0,
            /// <summary>AES 128bit(WinZIP互換)</summary>
            AES128 = 1,
            /// <summary>AES 192bit(WinZIP互換)</summary>
            AES192 = 2,
            /// <summary>AES 256bit(WinZIP互換)</summary>
            AES256 = 3
        }

        internal enum HeaderGeneralFlag : UInt16
        {
            ENCRYPTION = 0x0001,
            DESCRIPTION_PRESENT = 0x0008,
            UTF_ENCODING = 0x0800
        }

        internal enum HeaderComptype : UInt16
        {
            DEFLATE = 0x0008,
            AES_ENCRYPTION = 0x0063
        }

        internal enum HeaderNeedver : UInt16
        {
            STORED = 0x000a,
            DIRECTORY = 0x0014,
            DEFLATE = 0x0014,
            ZIPENC = 0x0014,
            AESENC = 0x0014,
            ZIP64 = 0x002d
        }

        #endregion

        #region "CONST"

        /// <summary>CentralFileHeaderSignature</summary>
        internal const uint SIG_PK0102 = 0x02014B50u;

        /// <summary>LocalFileHeaderSignature</summary>
        internal const uint SIG_PK0304 = 0x04034B50u;

        /// <summary>EndOfCentralDirectorySignature</summary>
        internal const uint SIG_PK0506 = 0x06054B50u;

        /// <summary>Zip64EndOfCentralDirectorySignature</summary>
        internal const uint SIG_PK0606 = 0x06064B50u;

        /// <summary>Zip64EndOfCentralDirectoryLocatorSignature</summary>
        internal const uint SIG_PK0607 = 0x07064B50u;

        /// <summary>DataDescriptorHeaderSignature</summary>
        internal const uint SIG_PK0708 = 0x08074B50u;

        /// <summary>CentralDirectryに設定するmade ver</summary>
        /// <remarks>
        /// 上位バイトはfile attributeの互換性。下位バイトはZIP仕様書のバージョン。
        /// 上位バイト 0:DOS(FAT/FAT32...),3:unix,7:mac,10:NTFS,...  
        ///   現状directoryとarchive属性にしか対応しないので0のDOSを設定
        /// 下位バイト ver6.3 を設定
        /// </remarks>
        private const uint HDR_MADEVER = 0x003fu;

        ///// <summary>
        ///// デフォルト最大多重度
        ///// 圧縮/暗号化は並列実行されるが、ZIP書庫への書き込みは一多重なのであまり上げてもトータルの時間はそれ程短くならない
        ///// コア数が多くてもデフォルトでは4多重までにしておく
        ///// </summary>
        //private const int MAX_SEMAPHORE_COUNT = 4;

        private const int READ_BUFFER_SIZE = 8192;
        private const int WRITE_BUFFER_SIZE = 32768;

        #endregion

        #region "メンバ変数"

        /// <summary>Zip書庫出力用ストリーム</summary>
        private WriteCountStream baseStream = null;

        /// <summary>Zip書庫出力用ファイルストリーム(zipファイルへの出力時に使用)</summary>
        private FileStream baseFileStream = null;

        /// <summary>パート情報格納用リスト</summary>
        private readonly Queue<PartInfoClass> partInfoList = new Queue<PartInfoClass>();

        /// <summary>ディレクトリ辞書(格納ディレクトリ名)</summary>
        private readonly HashSet<string> dirDic = new HashSet<string>();

        /// <summary>ディレクトリ辞書(入力されたディレクトリ)</summary>
        private readonly Dictionary<string, string> dirDicOrg = new Dictionary<string, string>();

        /// <summary>ファイル名・パスワードのエンコーディング</summary>
        private Encoding filenameEncoding = null;

        /// <summary>Finish実行済み判定</summary>
        private bool isFinished = false;

        /// <summary>TemporaryStream管理</summary>
        private TempStreamManage tsm = null;

        /// <summary>ディレクトリの区切り文字</summary>
        private char[] dirSplitChars = null;

        /// <summary>streamの破棄が必要かのフラグ</summary>
        private readonly bool isStreamDispose = false;

        /// <summary>同時処理件数制御用セマフォ</summary>
        private readonly SemaphoreSlim addZipSemaphore = null;

        /// <summary>セマフォ件数</summary>
        private readonly int semaphoreCount = 0;

        /// <summary>処理中のファイル</summary>
        private readonly ConcurrentDictionary<PartInfoClass, bool> processingFiles = new ConcurrentDictionary<PartInfoClass, bool>();

        /// <summary>セントラルディレクトリのエントリ件数</summary>
        private int directoryEntries = 0;

        /// <summary>Password</summary>
        private byte[] bytePassword = null;

        private static readonly Regex dirReplaceRegex = new Regex(@"^[\/\\]+", RegexOptions.Compiled);

        private long closedZipFileSize = 0;

        #endregion

        #region "Property"

        /// <summary>
        /// 暗号化パスワード  Default:null
        /// <para>nullなら暗号化なし</para>
        /// <para><see cref="ZipFileNameEncoding"/>でEncodeされます。互換性のためにASCII文字推奨</para>
        /// </summary>
        public string Password
        {
            set
            {
                if (bytePassword != null && bytePassword.Length > 0)
                    Array.Clear(bytePassword, 0, bytePassword.Length);
                bytePassword = null;
                if (value == null)
                {
                    return;
                }
                byte[] p = ShareMethodClass.EncodingGetBytes(value, this.ZipFileNameEncoding);

                if (p.Length > 0xFFFF)
                {
                    throw new ArgumentException(Resources.ERRMSG_PASSWORD_LENGTH, nameof(value));
                }
                bytePassword = p;
            }
        }

        /// <summary>
        /// 圧縮オプション  Default:<see cref="COMPRESSION_OPTION.DEFLATE"/>
        /// </summary>
        /// <remarks></remarks>
        public COMPRESSION_OPTION CompressionOption { get; set; } = COMPRESSION_OPTION.DEFLATE;

        /// <summary>
        /// 暗号化方式(Default:TRADITIONAL)
        /// </summary>
        /// <remarks></remarks>
        public ENCRYPTION_OPTION EncryptionOption { get; set; } = ENCRYPTION_OPTION.TRADITIONAL;

        /// <summary>
        /// <see cref="System.IO.Compression.CompressionLevel"/>
        /// </summary>
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

        /// <summary>
        /// 格納するタイムスタンプ(Default:<see cref="StoreTimestampOption.Ntfs"/>)
        /// </summary>
        public StoreTimestampOption StoreTimestamp { get; set; } = StoreTimestampOption.Ntfs;

        /// <summary>
        /// ファイル名・パスワードのエンコーディング  Default:システム規定値(<see cref="System.Globalization.CultureInfo.CurrentUICulture"/>)
        /// <para>.NET Framework環境以外(例:.net Core)ではCodePagesEncodingProviderを登録しないとasciiとutf系しか利用できない。<br />
        /// それ以外のコードページを使用する場合はCodePagesEncodingProviderをしておく。</para>
        /// <code>
        /// System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        /// </code>
        /// </summary>
        /// <remarks></remarks>
        public Encoding ZipFileNameEncoding
        {
            get
            {
#if NET5_0_OR_GREATER
                filenameEncoding ??= ShareMethodClass.AnsiEncoding;
#else
if (filenameEncoding == null)
                {
                    filenameEncoding = ShareMethodClass.AnsiEncoding;
                }
#endif

                return filenameEncoding;
            }
            set
            {
                filenameEncoding = value;
            }
        }

        /// <summary>
        /// <see cref="ZipFileNameEncoding"/>がUTF-8以外の場合に、UTF-8のファイル名も出力する<br />
        /// 通常の<see cref="ZipFileNameEncoding"/>でEncodingされたファイル名の他に、Info-ZIP互換のUTF-8ファイル名を別途出力する。
        /// <para>Info-ZIP Unicode Path Extra Field (0x7075)を出力する</para>
        /// </summary>
        /// <remarks>
        /// zip仕様書(APPNOTE)だとpk0102/pk0304のファイルネームがUTF8の場合はExtraDataFieldを作成すべきでは無い(should not)。
        /// flagのbit11(Language encoding flag EFS))==trueを設定しておけとある。そのためZipFileNameEncodingがUTF8の場合はこのフラグを無視する。
        /// </remarks>
        public bool StoreUtf8Filename { get; set; } = true;

        /// <summary>
        /// 圧縮せずにそのまま格納する拡張子のパターンリスト  Default:指定なし
        /// <para>圧縮せずに格納する拡張子を正規表現で指定する</para>
        /// </summary>
        /// <returns></returns>
        public List<Regex> DontCompressExtRegExList { get; } = new List<Regex>();

        /// <summary>
        /// コメント
        /// </summary>
        public string Comment { get; set; } = null;

        /// <summary>
        /// ディレクトリを格納する
        /// </summary>
        public bool StoreDirectories { get; set; } = true;

        private bool _StoreInOrderAdded = true;

        /// <summary>
        /// 書庫に格納するファイル・ディレクトリを依頼順に格納する。初回のAdd系メソッド呼び出し前に設定する必要がある。
        /// </summary>
        /// <remarks>
        /// 圧縮タスクはAdd系メソッド呼び出し順に完了しないため、書庫への格納順がAdd系メソッドの呼び出し順と異なる可能性が高い。trueに設定した場合依頼順で格納する。
        /// 圧縮タスクが完了してもAdd系メソッド呼び出し順に格納するため、テンポラリファイル使用量や処理時間が増加する。
        /// </remarks>
        public bool StoreInOrderAdded
        {
            get
            {
                return _StoreInOrderAdded;

            }
            set
            {
                if (addQueueId > 1)
                {
                    // 既にAddQueueが呼び出されている場合は変更不可
                    return;
                }
                _StoreInOrderAdded = value;
            }
        }

        /// <summary>
        /// 書庫に格納済みのファイルとディレクトリの総数
        /// </summary>
        /// <returns></returns>
        public int ZipFileCount { get; private set; } = 0;

        /// <summary>
        /// 出力済のZIP書庫サイズ
        /// </summary>
        /// <returns></returns>
        public long ZipFileSize
        {
            get
            {
                if (this.baseStream == null)
                {
                    return closedZipFileSize;
                }
                return this.baseStream.Length;
            }
        }

        /// <summary>
        /// ZIPファイルへの書き込み待ち件数
        /// <para>部分的に大きなファイルが存在する場合や、処理多重度が高すぎてZIPファイルへの出力が間に合っていない場合に大きくなる</para>
        /// </summary>
        /// <returns></returns>
        public int WriteQueueCount => procQueueList.Count + partInfoDicCount + (this.writeStreamTask.IsCompleted ? 0 : 1);

        /// <summary>
        /// 実行中の圧縮プロセス数
        /// </summary>
        public int CompressionProcs => semaphoreCount - addZipSemaphore.CurrentCount;

        /// <summary>
        /// 処理中のファイルを返却<br />
        /// </summary>
        public string[] InProcessFilename
        {
            get
            {
                List<string> list = new List<string>();
                processingFiles?.Keys.ToList().ForEach(part => {
                    list.Add(part.FullName);
                });
                return list.ToArray();
            }
        }

        /// <summary>
        /// ファイル総件数
        /// </summary>
        public int TotalFileCount => directoryEntries;

#endregion

        #region "コンストラクタ"

        /// <summary>
        /// コンストラクタ 
        /// </summary>
        /// <param name="writeStream">
        /// ZIP書庫出力ストリーム
        /// </param>
        /// <remarks></remarks>
        public ZipArcClass(Stream writeStream): this(writeStream, 0, Path.GetTempPath()) { }

        /// <summary>
        /// コンストラクタ 
        /// </summary>
        /// <param name="zipFileName">出力するZIPファイル名(既存ファイルは上書きされます)</param>
        /// <remarks></remarks>
        public ZipArcClass(string zipFileName) : this(zipFileName, 0, Path.GetTempPath()) { }
        
        /// <summary>
        /// コンストラクタ (多重度、TemporaryDirectory指定)
        /// </summary>
        /// <param name="zipFileName">出力するZIPファイル名(既存ファイルは上書きされます)</param>
        /// <param name="semaphoreCount">圧縮処理最大多重度(0の場合は<see cref="P:System.Environment.ProcessorCount" />)</param>
        /// <param name="tempDirPath">TemporaryFile作成ディレクトリのPath(nullの場合は<see cref="M:System.IO.Path.GetTempPath" />"から取得)</param>
        /// <remarks></remarks>
        public ZipArcClass(string zipFileName, int semaphoreCount, string tempDirPath)
        {
            this.semaphoreCount = (semaphoreCount <= 0) ? GetSemaphoreCount() : semaphoreCount;
            addZipSemaphore = new SemaphoreSlim(this.semaphoreCount);
            isStreamDispose = true;
            baseFileStream = new FileStream(zipFileName, FileMode.Create, FileAccess.Write, FileShare.None, WRITE_BUFFER_SIZE, FileOptions.SequentialScan | FileOptions.Asynchronous);
            Init(baseFileStream, string.IsNullOrWhiteSpace(tempDirPath) ? Path.GetTempPath() : tempDirPath);
        }

        /// <summary>
        /// コンストラクタ (多重度、TemporaryDirectory指定)
        /// </summary>
        /// <param name="writeStream">ZIP書庫出力ストリーム</param>
        /// <param name="semaphoreCount">圧縮処理最大多重度(0の場合は<see cref="P:System.Environment.ProcessorCount" />)</param>
        /// <param name="tempDirPath">TemporaryFile作成ディレクトリのPath(nullの場合は<see cref="M:System.IO.Path.GetTempPath" />"から取得)</param>
        /// <remarks></remarks>
        public ZipArcClass(Stream writeStream, int semaphoreCount, string tempDirPath)
        {
            this.semaphoreCount = (semaphoreCount <= 0) ? GetSemaphoreCount() : semaphoreCount;
            addZipSemaphore = new SemaphoreSlim(this.semaphoreCount);
            isStreamDispose = false;
            Init(writeStream, string.IsNullOrWhiteSpace(tempDirPath) ? Path.GetTempPath() : tempDirPath);
        }

        /// <summary>
        /// 初期化
        /// </summary>
        /// <param name="st"></param>
        /// <param name="tempDirPath"></param>
        private void Init(Stream st, string tempDirPath)
        {
            if (Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar)
            {
                dirSplitChars = new char[] { Path.DirectorySeparatorChar };
            }
            else
            {
                dirSplitChars = new char[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                };
            }
            if(Directory.Exists(tempDirPath) == false)
            {
                throw new DirectoryNotFoundException(string.Format(Resources.ERRMSG_TEMPDIR_NOTFOUND, tempDirPath));
            }
            tsm = new TempStreamManage(tempDirPath);

            baseStream = new WriteCountStream(st);
        }

        #endregion

        #region "格納系PublicMethods"

        #region "AddNewDirectory系"

        /// <summary>
        /// 空のディレクトリを追加する
        /// </summary>
        /// <param name="storeDirectoryName">追加するディレクトリ名</param>
        /// <remarks>
        /// </remarks>
        public void AddNewDirectory(string storeDirectoryName)
        {
            AddNewDirectory(storeDirectoryName, DateTime.Now);
        }

        /// <summary>
        /// 空のディレクトリを追加する
        /// </summary>
        /// <param name="storeDirectoryName">追加するディレクトリ名</param>
        /// <remarks>
        /// </remarks>
        public async Task AddNewDirectoryAsync(string storeDirectoryName)
        {
            await AddNewDirectoryAsync(storeDirectoryName, DateTime.Now).ConfigureAwait(false);
        }

        /// <summary>
        /// 空のディレクトリを追加する
        /// </summary>
        /// <param name="storeDirectoryName">追加するディレクトリ名</param>
        /// <param name="timeStamp">ディレクトリのタイムスタンプ</param>
        /// <remarks>
        /// </remarks>
        public void AddNewDirectory(string storeDirectoryName, DateTime timeStamp)
        {
            AddNewDirectory(storeDirectoryName, timeStamp, timeStamp, timeStamp);
        }

        /// <summary>
        /// 空のディレクトリを追加する
        /// </summary>
        /// <param name="storeDirectoryName">追加するディレクトリ名</param>
        /// <param name="timeStamp">ディレクトリのタイムスタンプ</param>
        /// <remarks>
        /// </remarks>
        public async Task AddNewDirectoryAsync(string storeDirectoryName, DateTime timeStamp)
        {
            await AddNewDirectoryAsync(storeDirectoryName, timeStamp, timeStamp, timeStamp).ConfigureAwait(false);
        }

        /// <summary>
        /// 空のディレクトリを追加する
        /// </summary>
        /// <param name="storeDirectoryName">追加するディレクトリ名</param>
        /// <param name="creationTimeStamp">作成日時</param>
        /// <param name="lastWriteTimeStamp">更新日時</param>
        /// <param name="lastAccessTimeStamp">アクセス日時</param>
        public void AddNewDirectory(string storeDirectoryName, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime lastAccessTimeStamp)
        {
            AddZipDirectory(storeDirectoryName, creationTimeStamp, lastWriteTimeStamp, lastAccessTimeStamp, TaskAbort.Create(), CancellationToken.None);
        }

        /// <summary>
        /// 空のディレクトリを追加する
        /// </summary>
        /// <param name="storeDirectoryName">追加するディレクトリ名</param>
        /// <param name="creationTimeStamp">作成日時</param>
        /// <param name="lastWriteTimeStamp">更新日時</param>
        /// <param name="lastAccessTimeStamp">アクセス日時</param>
        public Task AddNewDirectoryAsync(string storeDirectoryName, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime lastAccessTimeStamp)
        {
            AddZipDirectory(storeDirectoryName, creationTimeStamp, lastWriteTimeStamp, lastAccessTimeStamp, TaskAbort.Create(), CancellationToken.None);
            return Task.CompletedTask;
        }

        #endregion

        #region "AddDirectory系"

        /// <summary>
        /// 指定ディレクトリの配下のファイルをすべて書庫に格納する
        /// </summary>
        /// <param name="dirPath">格納するフォルダのパス</param>
        /// <remarks></remarks>
        public void AddDirectory(string dirPath)
        {
            AddDirectory(dirPath, null);
        }

        /// <summary>
        /// 指定ディレクトリの配下のファイルをすべて書庫に格納する
        /// </summary>
        /// <param name="dirPath">格納するフォルダのパス</param>
        /// <remarks></remarks>
        public async Task AddDirectoryAsync(string dirPath)
        {
            await AddDirectoryAsync(dirPath, null).ConfigureAwait(false);
        }

        /// <summary>
        /// 指定ディレクトリの配下のファイルをすべて書庫に格納する
        /// </summary>
        /// <param name="dirPath">格納するフォルダのパス</param>
        /// <param name="baseDir">書庫にbaseDirを作成して、配下にディレクトリ構成を格納する。</param>
        /// <remarks></remarks>
        public void AddDirectory(string dirPath, string baseDir)
        {
            AddDirectory(dirPath, baseDir, null, null);
        }

        /// <summary>
        /// 指定ディレクトリの配下のファイルをすべて書庫に格納する
        /// </summary>
        /// <param name="dirPath">格納するフォルダのパス</param>
        /// <param name="baseDir">書庫にbaseDirを作成して、配下にディレクトリ構成を格納する。</param>
        /// <remarks></remarks>
        public async Task AddDirectoryAsync(string dirPath, string baseDir)
        {
            await AddDirectoryAsync(dirPath, baseDir, null, null).ConfigureAwait(false);
        }

        /// <summary>
        /// 指定ディレクトリの配下のファイルをすべて書庫に格納する
        /// </summary>
        /// <param name="dirPath">格納するフォルダのパス</param>
        /// <param name="baseDir">書庫にbaseDirを作成して、配下にディレクトリ構成を格納する。</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddDirectoryAsync(string dirPath, string baseDir, CancellationToken cancelToken)
        {
            await AddDirectoryAsync(dirPath, baseDir, null, null, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 指定ディレクトリの配下のファイルをすべて書庫に格納する
        /// </summary>
        /// <param name="dirPath">格納するフォルダのパス</param>
        /// <param name="baseDir">書庫にbaseDirを作成して、配下にディレクトリ構成を格納する。</param>
        /// <param name="excludeFileNameList">格納対象外とするファイル名の正規表現リスト</param>
        /// <param name="excludeDirectoryNameList">格納対象外とするディレクトリ名の正規表現リスト</param>
        /// <remarks></remarks>
        public void AddDirectory(string dirPath, string baseDir, List<Regex> excludeFileNameList, List<Regex> excludeDirectoryNameList)
        {
            AddDirectoryAsync(dirPath, baseDir, excludeFileNameList, excludeDirectoryNameList).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 指定ディレクトリの配下のファイルをすべて書庫に格納する
        /// </summary>
        /// <param name="dirPath">格納するフォルダのパス</param>
        /// <param name="baseDir">書庫にbaseDirを作成して、配下にディレクトリ構成を格納する。</param>
        /// <param name="excludeFileNameList">格納対象外とするファイル名の正規表現リスト</param>
        /// <param name="excludeDirectoryNameList">格納対象外とするディレクトリ名の正規表現リスト</param>
        /// <remarks></remarks>
        public async Task AddDirectoryAsync(string dirPath, string baseDir, List<Regex> excludeFileNameList, List<Regex> excludeDirectoryNameList)
        {
            await AddDirectoryAsync(dirPath, baseDir, excludeFileNameList, excludeDirectoryNameList, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// 指定ディレクトリの配下のファイルをすべて書庫に格納する
        /// </summary>
        /// <param name="dirPath">格納するフォルダのパス</param>
        /// <param name="baseDir">書庫にbaseDirを作成して、配下にディレクトリ構成を格納する。</param>
        /// <param name="excludeFileNameList">格納対象外とするファイル名の正規表現リスト</param>
        /// <param name="excludeDirectoryNameList">格納対象外とするディレクトリ名の正規表現リスト</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddDirectoryAsync(string dirPath, string baseDir, List<Regex> excludeFileNameList, List<Regex> excludeDirectoryNameList, CancellationToken cancelToken)
        {
            DirectoryInfo di = new DirectoryInfo(dirPath);
            if (di.Exists == true)
            {
                // ディレクトリ内に格納するファイルがある場合は、ディレクトリを作成して格納する
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    // ディレクトリを作成しないで格納
                    await AddZipDirectoryRecursiveAsync(string.Empty, di, excludeFileNameList, excludeDirectoryNameList, cancelToken).ConfigureAwait(false);
                }
                else
                {
                    // ディレクトリを作成して格納
                    string zipBaseDir = dirReplaceRegex.Replace(baseDir, string.Empty);
                    await AddZipDirectoryRecursiveAsync($"{zipBaseDir}/", di, excludeFileNameList, excludeDirectoryNameList, cancelToken).ConfigureAwait(false);
                }
            }
            else
            {
                // ディレクトリなし
                throw new DirectoryNotFoundException(di.FullName);
            }
        }

        #endregion

        #region "AddFile系"

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するストリーム</param>
        /// <remarks></remarks>
        public void AddFileStream(string storeFileName, Stream storeStream)
        {
            AddFileStream(storeFileName, storeStream, DateTime.Now);
        }

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するストリーム</param>
        /// <remarks></remarks>
        public async Task AddFileStreamAsync(string storeFileName, Stream storeStream)
        {
            await AddFileStreamAsync(storeFileName, storeStream, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するストリーム</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFileStreamAsync(string storeFileName, Stream storeStream, CancellationToken cancelToken)
        {
            await AddFileStreamAsync(storeFileName, storeStream, DateTime.Now, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するファイルのストリーム</param>
        /// <param name="timeStamp">ファイルのタイムスタンプ</param>
        /// <remarks></remarks>
        public void AddFileStream(string storeFileName, Stream storeStream, DateTime timeStamp)
        {
            AddFileStream(storeFileName, storeStream, timeStamp, timeStamp, timeStamp);
        }

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するファイルのストリーム</param>
        /// <param name="timeStamp">ファイルのタイムスタンプ</param>
        /// <remarks></remarks>
        public async Task AddFileStreamAsync(string storeFileName, Stream storeStream, DateTime timeStamp)
        {
            await AddFileStreamAsync(storeFileName, storeStream, timeStamp, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するファイルのストリーム</param>
        /// <param name="timeStamp">ファイルのタイムスタンプ</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFileStreamAsync(string storeFileName, Stream storeStream, DateTime timeStamp, CancellationToken cancelToken)
        {
            await AddFileStreamAsync(storeFileName, storeStream, timeStamp, timeStamp, timeStamp, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するファイルのストリーム</param>
        /// <param name="creationTimeStamp">ファイルの作成日時</param>
        /// <param name="lastWriteTimeStamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimeStamp">ファイルのアクセス日時</param>
        /// <remarks></remarks>
        public void AddFileStream(string storeFileName, Stream storeStream, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime fileAccessTimeStamp)
        {
            Task.Run(async () =>
            {
                await AddFileStreamAsync(storeFileName, storeStream, creationTimeStamp, lastWriteTimeStamp, fileAccessTimeStamp).ConfigureAwait(false);
            }).GetAwaiter().GetResult();
            
        }

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するファイルのストリーム</param>
        /// <param name="creationTimeStamp">ファイルの作成日時</param>
        /// <param name="lastWriteTimeStamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimeStamp">ファイルのアクセス日時</param>
        /// <remarks></remarks>
        public async Task AddFileStreamAsync(string storeFileName, Stream storeStream, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime fileAccessTimeStamp)
        {
            await AddFileStreamAsync(storeFileName, storeStream, creationTimeStamp, lastWriteTimeStamp, fileAccessTimeStamp, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(Stream)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="storeStream">追加するファイルのストリーム</param>
        /// <param name="creationTimeStamp">ファイルの作成日時</param>
        /// <param name="lastWriteTimeStamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimeStamp">ファイルのアクセス日時</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFileStreamAsync(string storeFileName, Stream storeStream, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime fileAccessTimeStamp, CancellationToken cancelToken)
        {
            await AddZipFileAsync(storeFileName, storeStream, CompressionOption, EncryptionOption, creationTimeStamp, lastWriteTimeStamp, fileAccessTimeStamp, TaskAbort.Create(), cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <remarks></remarks>
        public void AddFileBytes(string storeFileName, byte[] byteData)
        {
            AddFileBytes(storeFileName, byteData, DateTime.Now);
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <remarks></remarks>
        public async Task AddFileBytesAsync(string storeFileName, byte[] byteData)
        {
            await AddFileBytesAsync(storeFileName, byteData, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFileBytesAsync(string storeFileName, byte[] byteData, CancellationToken cancelToken)
        {
            await AddFileBytesAsync(storeFileName, byteData, DateTime.Now, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <param name="timeStamp">ファイルのタイムスタンプ</param>
        /// <remarks></remarks>
        public void AddFileBytes(string storeFileName, byte[] byteData, DateTime timeStamp)
        {
            AddFileBytes(storeFileName, byteData, timeStamp, timeStamp, timeStamp);
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <param name="timeStamp">ファイルのタイムスタンプ</param>
        /// <remarks></remarks>
        public async Task AddFileBytesAsync(string storeFileName, byte[] byteData, DateTime timeStamp)
        {
            await AddFileBytesAsync(storeFileName, byteData, timeStamp, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <param name="timeStamp">ファイルのタイムスタンプ</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFileBytesAsync(string storeFileName, byte[] byteData, DateTime timeStamp, CancellationToken cancelToken)
        {
            await AddFileBytesAsync(storeFileName, byteData, timeStamp, timeStamp, timeStamp, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <param name="creationTimeStamp">ファイルの作成日時</param>
        /// <param name="lastWriteTimeStamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimeStamp">ファイルのアクセス日時</param>
        /// <remarks></remarks>
        public void AddFileBytes(string storeFileName, byte[] byteData, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime fileAccessTimeStamp)
        {
            Task.Run(async () => {
                await AddFileBytesAsync(storeFileName, byteData, creationTimeStamp, lastWriteTimeStamp, fileAccessTimeStamp).ConfigureAwait(false);
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <param name="creationTimeStamp">ファイルの作成日時</param>
        /// <param name="lastWriteTimeStamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimeStamp">ファイルのアクセス日時</param>
        /// <remarks></remarks>
        public async Task AddFileBytesAsync(string storeFileName, byte[] byteData, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime fileAccessTimeStamp)
        {
            await AddFileBytesAsync(storeFileName, byteData, creationTimeStamp, lastWriteTimeStamp, fileAccessTimeStamp, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(バイト配列)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <param name="creationTimeStamp">ファイルの作成日時</param>
        /// <param name="lastWriteTimeStamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimeStamp">ファイルのアクセス日時</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFileBytesAsync(string storeFileName, byte[] byteData, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime fileAccessTimeStamp, CancellationToken cancelToken)
        {
            await AddZipFileAsync(storeFileName, byteData, CompressionOption, EncryptionOption, creationTimeStamp, lastWriteTimeStamp, fileAccessTimeStamp, TaskAbort.Create(), cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(ファイル名)
        /// </summary>
        /// <param name="filePath">追加するファイルのパス</param>
        /// <remarks></remarks>
        public void AddFilePath(string filePath)
        {
            AddFilePath(Path.GetFileName(filePath), filePath);
        }

        /// <summary>
        /// ファイルの追加を行う(ファイル名)
        /// </summary>
        /// <param name="filePath">追加するファイルのパス</param>
        /// <remarks></remarks>
        public async Task AddFilePathAsync(string filePath)
        {
            await AddFilePathAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(ファイル名)
        /// </summary>
        /// <param name="filePath">追加するファイルのパス</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFilePathAsync(string filePath, CancellationToken cancelToken)
        {
            await AddFilePathAsync(Path.GetFileName(filePath), filePath, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(ファイル名)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="filePath">追加するファイルのパス</param>
        /// <remarks></remarks>
        public void AddFilePath(string storeFileName, string filePath)
        {
            AddFileInfo(storeFileName, new FileInfo(filePath));
        }

        /// <summary>
        /// ファイルの追加を行う(ファイル名)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="filePath">追加するファイルのパス</param>
        /// <remarks></remarks>
        public async Task AddFilePathAsync(string storeFileName, string filePath)
        {
            await AddFilePathAsync(storeFileName, filePath, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(ファイル名)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="filePath">追加するファイルのパス</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFilePathAsync(string storeFileName, string filePath, CancellationToken cancelToken)
        {
            await AddFileInfoAsync(storeFileName, new FileInfo(filePath), cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(FileInfo)
        /// </summary>
        /// <param name="fileInfo">FileInfo</param>
        /// <remarks></remarks>
        public void AddFileInfo(FileInfo fileInfo)
        {
            AddFileInfo(fileInfo.Name, fileInfo);
        }

        /// <summary>
        /// ファイルの追加を行う(FileInfo)
        /// </summary>
        /// <param name="fileInfo">FileInfo</param>
        /// <remarks></remarks>
        public async Task AddFileInfoAsync(FileInfo fileInfo)
        {
            await AddFileInfoAsync(fileInfo, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(FileInfo)
        /// </summary>
        /// <param name="fileInfo">FileInfo</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFileInfoAsync(FileInfo fileInfo, CancellationToken cancelToken)
        {
            await AddFileInfoAsync(fileInfo.Name, fileInfo, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(FileInfo)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="fileInfo">FileInfo</param>
        /// <remarks></remarks>
        public void AddFileInfo(string storeFileName, FileInfo fileInfo)
        {
            Task.Run(async () =>
            {
                await AddFileInfoAsync(storeFileName, fileInfo).ConfigureAwait(false);
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// ファイルの追加を行う(FileInfo)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="fileInfo">FileInfo</param>
        /// <remarks></remarks>
        public async Task AddFileInfoAsync(string storeFileName, FileInfo fileInfo)
        {
            await AddFileInfoAsync(storeFileName, fileInfo, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルの追加を行う(FileInfo)
        /// </summary>
        /// <param name="storeFileName">格納ファイル名</param>
        /// <param name="fileInfo">FileInfo</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public async Task AddFileInfoAsync(string storeFileName, FileInfo fileInfo, CancellationToken cancelToken)
        {
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException(fileInfo.FullName);
            }

            if (fileInfo.FullName.Equals(fileInfo.FullName.TrimEnd()))
            {
                await AddZipFileAsync(storeFileName, fileInfo, CompressionOption, EncryptionOption, fileInfo.CreationTime, fileInfo.LastWriteTime, fileInfo.LastAccessTime, cancelToken).ConfigureAwait(false);
            }
            else
            {
                // 最後がSPACEで終了しているファイル名対応
                await AddZipFileAsync(storeFileName, new FileInfo($"{fileInfo.FullName}."), CompressionOption, EncryptionOption, fileInfo.CreationTime, fileInfo.LastWriteTime, fileInfo.LastAccessTime, cancelToken).ConfigureAwait(false);
            }

        }

        #endregion

        #region "Finish"

        /// <summary>
        /// ZIP書庫の作成を終了する
        /// <para>ZIP書庫作成終了時に必ず呼び出すこと。ファイルに出力している場合、出力ファイルのクローズを行う</para>
        /// </summary>
        /// <remarks>
        /// </remarks>
        public void Finish()
        {
            Task.Run(async () => {
                await FinishAsync().ConfigureAwait(false); 
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// ZIP書庫の作成を終了する
        /// <para>ZIP書庫作成終了時に必ず呼び出すこと。ファイルに出力している場合、出力ファイルのクローズを行う</para>
        /// </summary>
        /// <remarks>
        /// </remarks>
        public async Task FinishAsync()
        {
            await this.FinishAsync(CancellationToken.None);
        }

        /// <summary>
        /// ZIP書庫の作成を終了する
        /// <para>ZIP書庫作成終了時に必ず呼び出すこと。ファイルに出力している場合、出力ファイルのクローズを行う</para>
        /// </summary>
        /// <remarks>
        /// </remarks>
        public async Task FinishAsync(CancellationToken cancelToken)
        {
            // Finish済チェック
            if (isFinished)
            {
                throw new InvalidOperationException(Resources.ERRMSG_ALREDY_FINISHED);
            }

            if (CompressionProcs > 0)
            {
                throw new InvalidOperationException(Resources.ERRMSG_TASK_RUNNING);
            }

            cancelToken.ThrowIfCancellationRequested();

            isFinished = true;

#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : FinishAsync() start");
            Stopwatch stopwatch = Stopwatch.StartNew();
#endif

            // 書庫作成終了処理
            try
            {
                // queueの書き込みタスクをwait
                await writeStreamTask.ConfigureAwait(false); 
                if (writeStreamTask.Exception != null)
                {
                    throw writeStreamTask.Exception;
                }
                writeStreamTask.Dispose();

                // queueに残りが無いか念のため処理する
                await WriteBaseStreamAsync(TaskAbort.Create(), cancelToken).ConfigureAwait(false);
                if (writeStreamTask.Exception != null)
                {
                    throw writeStreamTask.Exception;
                }
                if (partInfoDic.Count > 0)
                {
                    // queueに残りがある場合、例外を投げる
                    throw new InvalidOperationException(Resources.ERRMSG_INTERNAL_QUEUE_ERROR);
                }

                // CentoralDirectoryの開始位置待避
                long centralDirPos = baseStream.Position;

                // PK0102(CentoralDirectory)出力
                long centralDirLength = 0L;
                long centralDirCount = partInfoList.Count;

                while (partInfoList.Count > 0)
                {
                    PartInfoClass partInfo=partInfoList.Dequeue();
                    byte[] pk0102Bytes = partInfo.Pk0102Header.GetBytes();
#if NET6_0_OR_GREATER
                    await baseStream.WriteAsync(new ReadOnlyMemory<byte>(pk0102Bytes), cancelToken).ConfigureAwait(false);
#else
                    await baseStream.WriteAsync(pk0102Bytes, 0, pk0102Bytes.Length, cancelToken).ConfigureAwait(false);
#endif
                    centralDirLength += pk0102Bytes.LongLength;
                }

                // PK0506(EndOfCentralDirectory) 編集
                ZipHeader.PK0506Info pk0506Header = EditPK0506(centralDirPos, centralDirLength, centralDirCount);

                // PK0606及びPK0607の作成判定
                bool writeZip64EndOfCentralDirectory = false;
                if (centralDirCount >= UInt16.MaxValue ||
                    centralDirLength >= UInt32.MaxValue ||
                    centralDirPos >= UInt32.MaxValue)
                {
                    // 格納件数65535件以上
                    // ディレクトリ長0xFFFFFFFF以上(4G)
                    // 開始位置0xFFFFFFFF以上(4G)
                    writeZip64EndOfCentralDirectory = true;
                }

                // 編集条件を満たした場合、PK0606とPK0607編集
                if (writeZip64EndOfCentralDirectory)
                {
                    // PK0606 Zip64EndOfCentralDirectory
                    ZipHeader.PK0606Info pk0606 = new ZipHeader.PK0606Info();
                    pk0606.Signature = SIG_PK0606;
                    pk0606.Madever = (UInt16)0x2du;
                    pk0606.Needver = (UInt16)0x2du;
                    pk0606.Disknum = 0u;
                    pk0606.Startdisknum = 0u;
                    pk0606.Diskdirentry = (UInt64)centralDirCount;
                    pk0606.Direntry = (UInt64)centralDirCount;
                    pk0606.Dirsize = (UInt64)centralDirLength;
                    pk0606.Startpos = (UInt64)centralDirPos;

                    // PK0607 Zip64EndOfCentralDirectoryLocator
                    ZipHeader.PK0607Info pk0607 = new ZipHeader.PK0607Info();
                    pk0607.Signature = SIG_PK0607;
                    pk0607.Startdisknum = 0u;
                    pk0607.Startpos = (UInt64)baseStream.Position;
                    pk0607.Totaldisks = 1u;

                    // PK0606,PK0607出力
                    byte[] pk0606data = pk0606.GetBytes();
                    byte[] pk0607data = pk0607.GetBytes();
#if NET6_0_OR_GREATER
                    await baseStream.WriteAsync(new ReadOnlyMemory<byte>(pk0606data), cancelToken).ConfigureAwait(false);
                    await baseStream.WriteAsync(new ReadOnlyMemory<byte>(pk0607data), cancelToken).ConfigureAwait(false);
#else
                    await baseStream.WriteAsync(pk0606data, 0, pk0606data.Length, cancelToken).ConfigureAwait(false);
                    await baseStream.WriteAsync(pk0607data, 0, pk0607data.Length, cancelToken).ConfigureAwait(false);
#endif
                }

                // PK0506(EndOfCentralDirectory)出力
                byte[] pk0506data = pk0506Header.GetBytes();
#if NET6_0_OR_GREATER
                await baseStream.WriteAsync(new ReadOnlyMemory<byte>(pk0506data), cancelToken).ConfigureAwait(false);
#else
                await baseStream.WriteAsync(pk0506data, 0, pk0506data.Length, cancelToken).ConfigureAwait(false);
#endif
                await baseStream.FlushAsync(cancelToken).ConfigureAwait(false);

                closedZipFileSize = baseStream.WriteCount; // 出力バイト数を取得

            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                // baseStreamは出力バイトカウント用のWriteCountStreamなので、Disposeしない
                baseStream = null;

                // baseFileStreamはファイル名指定で初期化時にファイルを作成した場合、Disposeする
                if (isStreamDispose)
                {
                    baseFileStream?.Dispose();
                    baseFileStream = null;
                }
            }

#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : FinishAsync() end elapsed={stopwatch.ElapsedMilliseconds}ms");
#endif

        }

        /// <summary>
        /// PK0506(EndOfCentralDirectory) 編集
        /// </summary>
        /// <param name="centralDirPos"></param>
        /// <param name="centralDirLength"></param>
        /// <param name="centralDirCount"></param>
        /// <returns></returns>
        private ZipHeader.PK0506Info EditPK0506(long centralDirPos, long centralDirLength, long centralDirCount)
        {
            ZipHeader.PK0506Info pk0506Header = new ZipHeader.PK0506Info();
            pk0506Header.Signature = SIG_PK0506;
            pk0506Header.Disknum = 0;
            pk0506Header.Startdisknum = 0;

            // 格納件数編集
            if (centralDirCount >= UInt16.MaxValue)
            {
                // 格納件数件0xffff以上
                pk0506Header.Diskdirentry = UInt16.MaxValue;
                pk0506Header.Direntry = UInt16.MaxValue;
            }
            else
            {
                // 格納件数0xffff件未満
                pk0506Header.Diskdirentry = (UInt16)centralDirCount;
                pk0506Header.Direntry = (UInt16)centralDirCount;
            }

            // CentralDirectory長編集
            if (centralDirLength >= UInt32.MaxValue)
            {
                // ディレクトリ長0xffffffff以上(4G)
                pk0506Header.Dirsize = UInt32.MaxValue;
            }
            else
            {
                // ディレクトリ長0xffffffff未満(4G)
                pk0506Header.Dirsize = (UInt32)centralDirLength;
            }

            // CentralDirectory開始位置
            if (centralDirPos >= UInt32.MaxValue)
            {
                // 開始位置0xffffffff以上(4G)
                pk0506Header.Startpos = UInt32.MaxValue;
            }
            else
            {
                // 開始位置0xffffffff未満(4G)
                pk0506Header.Startpos = (UInt32)centralDirPos;
            }

            // コメント
            if (string.IsNullOrEmpty(this.Comment) == true)
            {
                pk0506Header.Comment = null;
            }
            else
            {
                byte[] commentByte = ShareMethodClass.EncodingGetBytes(this.Comment, this.ZipFileNameEncoding);
                if(commentByte.Length > Int16.MaxValue)
                {
                    Array.Resize(ref commentByte, Int16.MaxValue);
                }
                pk0506Header.Comment = commentByte;
            }
            return pk0506Header;
        }

#endregion

#endregion

        #region "PrivateMethod"

        #region "Common"

        /// <summary>
        /// デフォルトの多重度取得
        /// </summary>
        /// <returns></returns>
        private static int GetSemaphoreCount()
        {
            int semCount = Environment.ProcessorCount / 2;
            if (semCount == 0)
            {
                return 1;
            }
            return semCount;
        }

        #endregion

        #region "書庫追加系"

        /// <summary>
        /// ディレクトリを再帰的に書庫に追加する
        /// </summary>
        /// <param name="baseDir">書庫に格納する親ディレクトリ</param>
        /// <param name="di">格納するファイルorディレクトリ</param>
        /// <param name="excludeDirectoryNameList">格納対象外とするディレクトリの正規表現リスト(一致するディレクトリ以下が格納対象外となる))</param>
        /// <param name="excludeFileNameList">格納対象外とするファイルの正規表現リスト</param>
        /// <param name="cancelToken"></param>
        /// <remarks></remarks>
        private Task AddZipDirectoryRecursiveAsync(string baseDir,
                                                  DirectoryInfo di,
                                                  List<Regex> excludeFileNameList,
                                                  List<Regex> excludeDirectoryNameList,
                                                  CancellationToken cancelToken)
        {
#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : AddZipDirectoryRecuriveAsync start.");
#endif

            Task t = Task.Run(async () =>
            {

                List<Task> taskList = AddZipDirectoryRecursiveMainAsync(baseDir, di, excludeFileNameList, excludeDirectoryNameList, TaskAbort.Create(), cancelToken);
                await Task.WhenAll(taskList.ToArray()).ConfigureAwait(false);

            }, cancelToken);
#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : AddZipDirectoryRecuriveAsync async task end.");
#endif

#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : AddZipDirectoryRecuriveAsync end.");
#endif
            return t;
        }

        /// <summary>
        /// ディレクトリを再帰的に書庫に追加する
        /// </summary>
        /// <param name="baseDir"></param>
        /// <param name="di"></param>
        /// <param name="excludeFileNameList"></param>
        /// <param name="excludeDirectoryNameList"></param>
        /// <param name="abort"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        private List<Task> AddZipDirectoryRecursiveMainAsync(string baseDir,
                                                      DirectoryInfo di,
                                                      List<Regex> excludeFileNameList,
                                                      List<Regex> excludeDirectoryNameList,
                                                      TaskAbort abort,
                                                      CancellationToken cancelToken)
        {
            List<Task> taskList = new List<Task>();

            // Directoryとファイルのリスト取得
            List<DirectoryInfo> dirList=new List<DirectoryInfo> { };
            dirList.AddRange(di.GetDirectories());
            List<FileInfo> fileList = new List<FileInfo>();
            fileList.AddRange(di.GetFiles());

            // 空ディレクトリの場合はディレクトリのみ格納する
            if (fileList.Count == 0 && dirList.Count == 0)
            {
                AddZipDirectory(baseDir, di.CreationTime, di.LastWriteTime, di.LastAccessTime, abort, cancelToken, this.StoreDirectories);
                return taskList;
            }

            // 念のためソートする
            dirList.Sort((a, b) =>
            {
                return string.Compare(a.Name, b.Name);
            });
            fileList.Sort((a, b) =>
            {
                return string.Compare(a.Name, b.Name);
            });

            // ファイルの処理
            foreach (FileInfo targetFile in fileList)
            {
                cancelToken.ThrowIfCancellationRequested();

                // 対象外ファイル判定
                if (excludeFileNameList != null && excludeFileNameList.Count > 0)
                {
                    bool isExclude = false;
                    foreach (Regex reg in excludeFileNameList)
                    {
                        if (reg.IsMatch(targetFile.Name))
                        {
                            isExclude = true;
                            break;
                        }
                    }
                    if (isExclude)
                    {
                        continue;
                    }
                }

                // ファイルの格納
                string subFile = $"{baseDir}{targetFile.Name}";

                // AddZipFileAsyncを呼び出して非同期でファイルを追加        
                taskList.Add(AddZipFileAsync(subFile, targetFile, CompressionOption, EncryptionOption, targetFile.CreationTime, targetFile.LastWriteTime, targetFile.LastAccessTime, abort, cancelToken));
            }

            // ディレクトリの処理
            foreach (DirectoryInfo targetDirectory in dirList)
            {
                cancelToken.ThrowIfCancellationRequested();

                // 格納対象外ディレクトリ判定
                if (excludeDirectoryNameList != null && excludeDirectoryNameList.Count > 0)
                {
                    bool isExclude = false;
                    foreach (Regex reg in excludeDirectoryNameList)
                    {
                        if (reg.IsMatch(targetDirectory.Name))
                        {
                            isExclude = true;
                            break;
                        }
                    }
                    if (isExclude)
                    {
                        continue;
                    }
                }

                // サブディレクトリを再帰的に処理
                string subDir = $"{baseDir}{targetDirectory.Name}/";
                //AddZipDirectory(subDir, targetDirectory.CreationTime, targetDirectory.LastWriteTime, targetDirectory.LastAccessTime, abort, cancelToken, this.StoreDirectories);
                taskList.AddRange(AddZipDirectoryRecursiveMainAsync(subDir, targetDirectory, excludeFileNameList, excludeDirectoryNameList, abort, cancelToken));
            }
            return taskList;
        }

        /// <summary>
        /// 空のディレクトリを追加する
        /// </summary>
        /// <param name="directoryName">追加するディレクトリ名</param>
        /// <param name="creationTimeStamp">ファイルの作成日時</param>
        /// <param name="lastWriteTimeStamp">ファイルの更新日時</param>
        /// <param name="lastAccessTimeStamp">ファイルのアクセス日時</param>
        /// <param name="cancelToken"></param>
        /// <param name="abort"></param>
        /// <param name="store"></param>
        /// <remarks>正規化後の書庫ルートからの親ディレクトリ</remarks>
        private string AddZipDirectory(string directoryName, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime lastAccessTimeStamp, TaskAbort abort, CancellationToken cancelToken, bool store = true)
        {

            // 引数チェック
            directoryName = SanitizeEntryName(directoryName);

            lock (dirDicOrg)
            {
                // 既に処理済みなら格納ディレクトリ名の返却のみ
                if (dirDicOrg.TryGetValue(directoryName, out string value))
                {
                    return value;
                }

                // ディレクトリ単位に区切る
                string[] dirArray = directoryName.Split(dirSplitChars);
                StringBuilder arcDirectoryName = new StringBuilder();

                // ディレクトリ毎に分割して処理
                foreach (string dirName in dirArray)
                {
                    // Emptyチェック(最初と最後の/対応)
                    if (string.IsNullOrEmpty(dirName) == true)
                    {
                        continue;
                    }

                    // ディレクトリ名の使用不可文字チェック
                    if (ShareMethodClass.CheckPathName(dirName) == false)
                    {
                        throw new ArgumentException(string.Format(Resources.ERRMSG_INVALID_DIRNAME, directoryName));
                    }

                    // 書庫にディレクトリ追加
                    arcDirectoryName.Append($"{dirName}/");
                    if (store == true)
                        AddZipDirectorySub(arcDirectoryName.ToString(), creationTimeStamp, lastWriteTimeStamp, lastAccessTimeStamp, abort, cancelToken);
                
                }

                // 辞書に追加
                dirDicOrg.Add(directoryName, arcDirectoryName.ToString());

                // 格納したディレクトリを返却
                return arcDirectoryName.ToString();
            }
        }

        /// <summary>
        /// ディレクトリを追加する(Sub)
        /// </summary>
        /// <param name="directoryName">追加するディレクトリ名</param>
        /// <param name="creationTimeStamp">ファイルの作成日時</param>
        /// <param name="lastWriteTimeStamp">ファイルの更新日時</param>
        /// <param name="lastAccessTimeStamp">ファイルのアクセス日時</param>
        /// <param name="cancelToken"></param>
        /// <param name="abort"></param>
        /// <remarks></remarks>
        private void AddZipDirectorySub(string directoryName, DateTime creationTimeStamp, DateTime lastWriteTimeStamp, DateTime lastAccessTimeStamp, TaskAbort abort, CancellationToken cancelToken)
        {

            lock (dirDic)
            {
                // 既に追加済なら終了する
                if (dirDic.Contains(directoryName) == true)
                {
                    return;
                }
                dirDic.Add(directoryName);

                // PartInfo のディレクトリ用編集
                PartInfoClass partInfo = new PartInfoClass();
                partInfo.Id = Interlocked.Increment(ref directoryEntries);
                partInfo.FileName = ShareMethodClass.EncodingGetBytes(directoryName, ZipFileNameEncoding);
                partInfo.FullName = directoryName;
                partInfo.FileAttribute = FileAttributes.Directory;
                partInfo.FileCreateTimeStamp = creationTimeStamp;
                partInfo.FileModifyTimeStamp = lastWriteTimeStamp;
                partInfo.FileAccessTimeStamp = lastAccessTimeStamp;
                partInfo.Password = null;
                partInfo.CompressionOption = COMPRESSION_OPTION.NOT_COMPRESSED;
                partInfo.EncryptionOption = ENCRYPTION_OPTION.TRADITIONAL;

                // ディレクトリ格納
                AddZipDirectorySub(partInfo, abort, cancelToken);

            }
        }

        private Task AddZipFileAsync(string fileName,
                             FileInfo fi,
                             COMPRESSION_OPTION compressionOption,
                             ENCRYPTION_OPTION encryptionOption,
                             DateTime fileCreateTimestamp,
                             DateTime fileModifyTimestamp,
                             DateTime fileAccessTimestamp,
                             CancellationToken cancelToken)
        {
            return AddZipFileAsync(fileName, fi, compressionOption, encryptionOption, fileCreateTimestamp, fileModifyTimestamp, fileAccessTimestamp, TaskAbort.Create(), cancelToken);
        }

        /// <summary>
        /// 書庫にファイルの追加を行う(FileInfo)
        /// </summary>
        /// <param name="storeFileName">ファイル名</param>
        /// <param name="fi">追加するファイルの情報</param>
        /// <param name="compressionOption"></param>
        /// <param name="encryptionOption"></param>
        /// <param name="fileCreateTimestamp">ファイルの作成日時</param>
        /// <param name="fileModifyTimestamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimestamp">ファイルのアクセス日時</param>
        /// <param name="abort"></param>
        /// <param name="cancelToken"></param>
        /// <remarks></remarks>
        private async Task AddZipFileAsync(string storeFileName,
                                     FileInfo fi,
                                     COMPRESSION_OPTION compressionOption,
                                     ENCRYPTION_OPTION encryptionOption,
                                     DateTime fileCreateTimestamp,
                                     DateTime fileModifyTimestamp,
                                     DateTime fileAccessTimestamp,
                                     TaskAbort abort,
                                     CancellationToken cancelToken)
        {


            PartInfoClass partInfo = EditPartInfo(storeFileName, compressionOption, encryptionOption, fileCreateTimestamp, fileModifyTimestamp, fileAccessTimestamp, abort, cancelToken);

            using (FileStream fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, READ_BUFFER_SIZE, FileOptions.SequentialScan | FileOptions.Asynchronous))
            {
                await this.AddZipFileAsyncSemaphore(partInfo, fs, abort, cancelToken).ConfigureAwait(false);
            }

        }

        /// <summary>
        /// 書庫にファイルの追加を行う(Byte配列)
        /// </summary>
        /// <param name="fileName">ファイル名</param>
        /// <param name="byteData">追加するバイト配列</param>
        /// <param name="compressionOption"></param>
        /// <param name="encryptionOption"></param>
        /// <param name="fileCreateTimestamp">ファイルの作成日時</param>
        /// <param name="fileModifyTimestamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimestamp">ファイルのアクセス日時</param>
        /// <param name="abort"></param>
        /// <param name="cancelToken"></param>
        /// <remarks></remarks>
        private async Task AddZipFileAsync(string fileName,
                                     byte[] byteData,
                                     COMPRESSION_OPTION compressionOption,
                                     ENCRYPTION_OPTION encryptionOption,
                                     DateTime fileCreateTimestamp,
                                     DateTime fileModifyTimestamp,
                                     DateTime fileAccessTimestamp,
                                     TaskAbort abort,
                                     CancellationToken cancelToken)
        {
            PartInfoClass partInfo = EditPartInfo(fileName, compressionOption, encryptionOption, fileCreateTimestamp, fileModifyTimestamp, fileAccessTimestamp, abort, cancelToken);

            using (MemoryStream ms = new MemoryStream(byteData, false))
            {
                await this.AddZipFileAsyncSemaphore(partInfo, ms, abort, cancelToken).ConfigureAwait(false);
            }

        }

        /// <summary>
        /// 書庫にファイルの追加を行う(FileStream)
        /// </summary>
        /// <param name="fileName">ファイル名</param>
        /// <param name="fileStream">追加するファイルのストリーム</param>
        /// <param name="compressionOption"></param>
        /// <param name="encryptionOption"></param>
        /// <param name="fileCreateTimestamp">ファイルの作成日時</param>
        /// <param name="fileModifyTimestamp">ファイルの更新日時</param>
        /// <param name="fileAccessTimestamp">ファイルのアクセス日時</param>
        /// <param name="abort"></param>
        /// <param name="cancelToken"></param>
        /// <remarks></remarks>
        private async Task AddZipFileAsync(string fileName,
                                     Stream fileStream,
                                     COMPRESSION_OPTION compressionOption,
                                     ENCRYPTION_OPTION encryptionOption,
                                     DateTime fileCreateTimestamp,
                                     DateTime fileModifyTimestamp,
                                     DateTime fileAccessTimestamp,
                                     TaskAbort abort,
                                     CancellationToken cancelToken)
        {

            PartInfoClass partInfo = EditPartInfo(fileName, compressionOption, encryptionOption, fileCreateTimestamp, fileModifyTimestamp, fileAccessTimestamp, abort, cancelToken);

            await this.AddZipFileAsyncSemaphore(partInfo, fileStream, abort, cancelToken).ConfigureAwait(false);

        }

        /// <summary>
        /// PartInfo編集
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="compressionOption"></param>
        /// <param name="encryptionOption"></param>
        /// <param name="fileCreateTimestamp"></param>
        /// <param name="fileModifyTimestamp"></param>
        /// <param name="fileAccessTimestamp"></param>
        /// <param name="cancelToken"></param>
        /// <param name="abort"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        private PartInfoClass EditPartInfo(string fileName, 
                                           COMPRESSION_OPTION compressionOption, 
                                           ENCRYPTION_OPTION encryptionOption, 
                                           DateTime fileCreateTimestamp, 
                                           DateTime fileModifyTimestamp, 
                                           DateTime fileAccessTimestamp, 
                                           TaskAbort abort, 
                                           CancellationToken cancelToken)
        {

            if(string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            // ファイル名のチェック
            fileName = SanitizeEntryName(fileName);

            // ディレクトリとファイル名の分離
            string arcFilePath = Path.GetDirectoryName(fileName);
            string arcFileName = Path.GetFileName(fileName);

            // ファイル名のチェック
            if (ShareMethodClass.CheckFileName(arcFileName) == false)
            {
                throw new ArgumentException(string.Format(Resources.ERRMSG_INVALID_FILENAME, arcFileName), nameof(fileName));
            }

            // ディレクトリに配置される場合はディレクトリを作成する
            if (string.IsNullOrEmpty(arcFilePath) == false)
            {
                arcFileName = $"{AddZipDirectory(arcFilePath, fileCreateTimestamp, fileModifyTimestamp, fileAccessTimestamp, abort, cancelToken, this.StoreDirectories)}{arcFileName}";
            }

            // PartInfo編集
            PartInfoClass partInfo = new PartInfoClass();
            partInfo.Id = Interlocked.Increment(ref directoryEntries);
            partInfo.FileName = ShareMethodClass.EncodingGetBytes(arcFileName, ZipFileNameEncoding);
            partInfo.FullName = arcFileName;
            partInfo.FileAttribute = FileAttributes.Archive;
            partInfo.FileCreateTimeStamp = fileCreateTimestamp;
            partInfo.FileModifyTimeStamp = fileModifyTimestamp;
            partInfo.FileAccessTimeStamp = fileAccessTimestamp;
            partInfo.CompressionOption = compressionOption;
            partInfo.CompressionLevel = this.CompressionLevel;
            partInfo.EncryptionOption = encryptionOption;
            partInfo.WriteDataDescriptor = false;

            if (this.bytePassword != null)
            {

                partInfo.Password = this.bytePassword;
            }

            // 圧縮対象外判定
            string ext = Path.GetExtension(arcFileName);
            if (!string.IsNullOrEmpty(ext) && DontCompressExtRegExList != null && DontCompressExtRegExList.Count > 0)
            {
                if (ext[0] == '.')
                {
                    ext = ext.Remove(0, 1);
                }
                foreach (Regex reg in DontCompressExtRegExList)
                {
                    if (reg.IsMatch(ext))
                    {
                        partInfo.CompressionOption = COMPRESSION_OPTION.NOT_COMPRESSED;
                        break;
                    }
                }
            }
            return partInfo;
        }

        /// <summary>
        /// ZIPエントリ名のサニタイズ
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private string SanitizeEntryName(string name)
        {

            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            // 先頭の / or \ を除去
            string work = dirReplaceRegex.Replace(name.Trim(), string.Empty)
                                         .Replace('\\', '/');

            // ドライブ指定（例: C:）や絶対パスは禁止
            if (Path.IsPathRooted(work) || (work.Length >= 2 && char.IsLetter(work[0]) && work[1] == ':'))
                throw new ArgumentException(string.Format(Resources.ERRMSG_ROOT_PATH, name), nameof(name));

            // . と .. を正規化
            var parts = work.Split(dirSplitChars, StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();
            foreach (var p in parts)
            {
                if (p == "." || p == "..")
                {
                    if (stack.Count == 0) throw new ArgumentException(string.Format(Resources.ERRMSG_DIR_TRAVERSAL_NOT_ALLOWED, name), nameof(name));
                    stack.Pop();
                    continue;
                }
                stack.Push(p);
            }
            return string.Join("/", stack.Reverse());
        }

        private async Task AddZipFileAsyncSemaphore(PartInfoClass partInfo, 
                                                    Stream fileStream, 
                                                    TaskAbort abort, 
                                                    CancellationToken cancelToken)
        {

#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : Semaphore WaitAsync. Count={addZipSemaphore.CurrentCount}");
            Stopwatch stp = Stopwatch.StartNew();
#endif

            // セマフォ獲得待ち
            try
            {

                // タスクキャンセル判定
                cancelToken.ThrowIfCancellationRequested();

                // 他スレッドの中断判定
                abort.ThrowIfAbortRequested();

                await this.addZipSemaphore.WaitAsync(cancelToken).ConfigureAwait(false);

                // タスクキャンセル判定
                cancelToken.ThrowIfCancellationRequested();

                // 他スレッドの中断判定
                abort.ThrowIfAbortRequested();

                // Finish済チェック
                if (isFinished)
                {
                    throw new InvalidOperationException(Resources.ERRMSG_ALREDY_FINISHED);
                }

            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (OperationCanceledException ex)
            {
                abort.Abort(ex);
#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : Semaphore Cancel Request. Count={this.addZipSemaphore.CurrentCount}, wait={stp.Elapsed.TotalMilliseconds}ms");
#endif
                return;
            }
            catch (Exception ex)
            {
                abort.Abort(ex);
                throw;
            }

#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : Semaphore Enters. Count={this.addZipSemaphore.CurrentCount}, wait={stp.Elapsed.TotalMilliseconds}ms");
            stp.Restart();
#endif

            try
            {

                // 処理中リストに追加
                processingFiles.TryAdd(partInfo, false);

#if DEBUG
                if (Path.GetFileName(partInfo.FullName) == "throwexception.txt")
                {
                    throw new IOException("Debug Throw Exception");
                }
#endif

                await this.AddZipFileAsyncSubAsync(partInfo, fileStream, abort, cancelToken).ConfigureAwait(false);
            }
            catch (TaskAbort.TaskAbortException) { return; }
            catch (Exception ex)
            {
                abort.Abort(ex);
                throw;
            }
            finally
            {

                // 処理中リストから削除
                processingFiles.TryRemove(partInfo, out bool dmy);

                // セマフォリリース
                int resCount = this.addZipSemaphore.Release();

#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : Semaphore Release. elapsed={stp.Elapsed.TotalMilliseconds}ms Count={resCount}");
#endif

            }

        }

        /// <summary>
        /// ZIP書庫の個別ディレクトリ作成サブ
        /// </summary>
        /// <param name="partInfo"></param>
        /// <param name="abort"></param>
        /// <param name="cancelToken"></param>
        private void AddZipDirectorySub(PartInfoClass partInfo, TaskAbort abort, CancellationToken cancelToken)
        {
            // Finish済チェック
            if (isFinished)
            {
                throw new InvalidOperationException(Resources.ERRMSG_ALREDY_FINISHED);
            }

            // DataDescriptor編集
            //ZipHeader.PK0708Info pk0708Work = new ZipHeader.PK0708Info();
            partInfo.Pk0708Header = new ZipHeader.PK0708Info();

            // ExtraData編集
            EditExtraData(partInfo);

            // PK0708編集
            //EditPK0708(partInfo, pk0708Work);

            // キュー追加
            AddQueue(partInfo, null, abort, cancelToken);
        }

        /// <summary>
        /// ZIP書庫の個別ファイル作成サブ(ファイル用)
        /// </summary>
        /// <param name="partInfo"></param>
        /// <param name="fileStream"></param>
        /// <param name="abort"></param>
        /// <param name="cancelToken"></param>
        private async Task AddZipFileAsyncSubAsync(PartInfoClass partInfo, Stream fileStream, TaskAbort abort, CancellationToken cancelToken)
        {

            // Finish済チェック
            if (isFinished)
            {
                throw new InvalidOperationException(Resources.ERRMSG_ALREDY_FINISHED);
            }

            // tempStream書き込み(圧縮、圧縮前後のサイズ取得、CRC32計算)
            Stream tempStream = tsm.GetTempStream(0);
            partInfo.Pk0708Header = await WriteTempStreamAsync(fileStream, tempStream, partInfo, cancelToken).ConfigureAwait(false);
            await tempStream.FlushAsync(cancelToken).ConfigureAwait(false);
            // 入力ファイルサイズ0byte対応
            if (partInfo.Pk0708Header.UncompsizeLong == 0)
            {
                partInfo.CompressionOption = COMPRESSION_OPTION.NOT_COMPRESSED;
                partInfo.Password = null;
                partInfo.Pk0708Header.Crc32 = 0;
                partInfo.Pk0708Header.CompsizeLong = 0;
                tempStream.Dispose();
                tempStream = Stream.Null;
            }

            // ExtraData編集
            EditExtraData(partInfo);

            try
            {
                // キャンセル判定
                cancelToken.ThrowIfCancellationRequested();

                // Abort Check
                abort.ThrowIfAbortRequested();
            }
            catch
            {
                tempStream.Dispose();
                throw;
            }

            // wirteStreamへの出力
            tempStream.Position = 0;
            if (partInfo.Password == null)
            {
                // 暗号化無し
                AddQueue(partInfo, tempStream, abort, cancelToken);
            }
            else
            {
                // 暗号化あり
                Stream tempStream2 = tsm.GetTempStream(tempStream.Length + 100);
                try
                {
                    switch (partInfo.EncryptionOption)
                    {
                        case ENCRYPTION_OPTION.TRADITIONAL:
                            // Traditional PKWARE Encryption
                            using (ZipCryptStream encryptionStream = new ZipCryptStream(tempStream2, partInfo.Password, partInfo.Pk0708Header.Crc32, ZipCryptStream.StreamMode.ENCRYPT))
                            {
                                await tempStream.CopyToAsync(encryptionStream, 81920, cancelToken).ConfigureAwait(false);
                                await encryptionStream.FlushAsync(cancelToken).ConfigureAwait(false);
                            }
                            break;
                        case ENCRYPTION_OPTION.AES128:
                        case ENCRYPTION_OPTION.AES192:
                        case ENCRYPTION_OPTION.AES256:
                            // AES暗号化
                            using (ZipAesCryptStream encryptionStream = new ZipAesCryptStream(tempStream2, partInfo.EncryptionOption, partInfo.Password, ZipAesCryptStream.StreamMode.ENCRYPT,tempStream.Length))
                            {
                                await tempStream.CopyToAsync(encryptionStream, 81920, cancelToken).ConfigureAwait(false);
                                await encryptionStream.FlushAsync(cancelToken).ConfigureAwait(false);
                            }
                            // AESの場合CRC32を記録しない(AE-2)
                            partInfo.Pk0708Header.Crc32 = 0;
                            break;
                    }
                    // Compsize
                    partInfo.Pk0708Header.CompsizeLong = tempStream2.Position;
                    tempStream2.Position = 0;
                    AddQueue(partInfo, tempStream2, abort, cancelToken);
                }
                catch
                {
                    tempStream2.Dispose();
                    throw;
                }
                finally
                {

                    tempStream.Dispose();
                   
                }
            }

        }

        /// <summary>
        /// テンポラリストリームに一ファイル分圧縮して出力する
        /// </summary>
        /// <returns></returns>
        /// <remarks></remarks>
        private async Task<ZipHeader.PK0708Info> WriteTempStreamAsync(Stream fileStream, Stream tempStream, PartInfoClass partInfo, CancellationToken cancelToken)
        {
            ZipHeader.PK0708Info pk0708 = new ZipHeader.PK0708Info();

            using (CalcCrc32Stream crcStream = new CalcCrc32Stream(fileStream, CalcCrc32Stream.StreamMode.READ))
            {
                switch (partInfo.CompressionOption)
                {
                    case COMPRESSION_OPTION.DEFLATE:
                        // Deflate圧縮
                        using (DeflateStream compressionStream = new DeflateStream(tempStream, this.CompressionLevel, true))
                        {
                            await crcStream.CopyToAsync(compressionStream, 81920, cancelToken).ConfigureAwait(false);
                            await compressionStream.FlushAsync(cancelToken).ConfigureAwait(false);
                        }
                        pk0708.UncompsizeLong = crcStream.IoCount;
                        pk0708.Crc32 = crcStream.Crc32;
                        break;
                    default:
                        // 圧縮無し
                        await crcStream.CopyToAsync(tempStream, 81920, cancelToken).ConfigureAwait(false);
                        await tempStream.FlushAsync(cancelToken).ConfigureAwait(false);
                        pk0708.UncompsizeLong = crcStream.IoCount;
                        pk0708.Crc32 = crcStream.Crc32;
                        break;
                }
                pk0708.CompsizeLong = tempStream.Position;
                return pk0708;
            }

        }

        #endregion

        #region "Header編集"

        /// <summary>
        /// ExtraData編集
        /// </summary>
        /// <param name="partInfo"></param>
        /// <remarks></remarks>
        private void EditExtraData(PartInfoClass partInfo)
        {

            // Info-ZIP Unicode Path Extra Field (0x7075)
            // Utf8FileNameEnabledが設定されていて、ZipFileEncodingがUTF8以外の場合にutf-8とエンコード結果が異なる場合に作成する
            if (this.StoreUtf8Filename && 
                this.ZipFileNameEncoding.CodePage != ShareMethodClass.Utf8Encoding.CodePage)
            {
                
                if (ShareMethodClass.ByteArrayCompare(partInfo.FileName,ShareMethodClass.EncodingGetBytes(partInfo.FullName, ShareMethodClass.Utf8Encoding)) == false)
                {
                    ZipHeader.UnicodePathExtraDataField unicodePath = new ZipHeader.UnicodePathExtraDataField();
                    unicodePath.UnicodeName = partInfo.FullName;
                    unicodePath.NameCrc32 = CalcCrc32Stream.CalcCrc(partInfo.FileName);
                    partInfo.UnicodePathExtraData = unicodePath;
                }
            }

            // NTFS時刻情報
            if (this.StoreTimestamp == StoreTimestampOption.Ntfs ||
                this.StoreTimestamp == StoreTimestampOption.NtfsAndExtendedTimestamp)
            {
                partInfo.NtfsTimestampExtraData = new ZipHeader.NtfsDateExtraDataInfo();
                partInfo.NtfsTimestampExtraData.CreateTime = partInfo.FileCreateTimeStamp.ToFileTime();
                partInfo.NtfsTimestampExtraData.AccessTime = partInfo.FileAccessTimeStamp.ToFileTime();
                partInfo.NtfsTimestampExtraData.ModifyTime = partInfo.FileModifyTimeStamp.ToFileTime();
            }

            // ExtendedTimestamp
            if (this.StoreTimestamp == StoreTimestampOption.ExtendedTimestamp ||
                this.StoreTimestamp == StoreTimestampOption.NtfsAndExtendedTimestamp)
            {
                partInfo.ExtendedTimestampExtraData = new ZipHeader.ExtendedTimestampInfo();
                partInfo.ExtendedTimestampExtraData.CreateTime = partInfo.FileCreateTimeStamp;
                partInfo.ExtendedTimestampExtraData.AccessTime = partInfo.FileAccessTimeStamp;
                partInfo.ExtendedTimestampExtraData.ModifyTime = partInfo.FileModifyTimeStamp;
            }

            // AES暗号化情報
            if (partInfo.Password != null && (
                partInfo.EncryptionOption == ENCRYPTION_OPTION.AES128 ||
                partInfo.EncryptionOption == ENCRYPTION_OPTION.AES192 ||
                partInfo.EncryptionOption == ENCRYPTION_OPTION.AES256))
            {
                partInfo.AesExtraData = new ZipHeader.AesExtraDataInfo();
                switch (partInfo.CompressionOption)
                {
                    case COMPRESSION_OPTION.NOT_COMPRESSED:
                        partInfo.AesExtraData.Comptype = 0;
                        break;
                    case COMPRESSION_OPTION.DEFLATE:
                        partInfo.AesExtraData.Comptype = (ushort)HeaderComptype.DEFLATE;
                        break;
                }
                partInfo.AesExtraData.EncStrength = (ushort)partInfo.EncryptionOption;
            }
        }

        /// <summary>
        /// Zip64ExtraDataInfoの編集
        /// </summary>
        /// <param name="partInfo"></param>
        /// <remarks></remarks>
        private static void EditZip64ExtraData(PartInfoClass partInfo)
        {

            // Zip64ExtraData作成判定
            // LocalHeader(PK0304)の開始位置または圧縮前後のサイズが0xffff以上なら作成する
            if (partInfo.Pk0304HeaderPos >= uint.MaxValue ||
                partInfo.Pk0708Header.CompsizeLong >= uint.MaxValue ||
                partInfo.Pk0708Header.UncompsizeLong >= uint.MaxValue)
            {
                partInfo.Zip64ExtraData = new ZipHeader.Zip64ExtraDataInfo();

                partInfo.Zip64ExtraData.Uncompsize = (ulong)partInfo.Pk0708Header.UncompsizeLong;
                partInfo.Zip64ExtraData.Compsize = (ulong)partInfo.Pk0708Header.CompsizeLong;
                partInfo.Zip64ExtraData.HeaderOffset = (ulong)partInfo.Pk0304HeaderPos;

            }
        }

        /// <summary>
        /// セントラルディレクトリ編集
        /// </summary>
        /// <param name="partInfo"></param>
        /// <remarks></remarks>
        private static void EditPK0102(PartInfoClass partInfo)
        {

            partInfo.Pk0102Header = new ZipHeader.PK0102Info();
            partInfo.Pk0102Header.Signature = SIG_PK0102;
            partInfo.Pk0102Header.Madever = (ushort)HDR_MADEVER;
            partInfo.Pk0102Header.Opt = partInfo.Pk0304Header.Opt;
            partInfo.Pk0102Header.Comptype = partInfo.Pk0304Header.Comptype;
            partInfo.Pk0102Header.Filetime = partInfo.Pk0304Header.Filetime;
            partInfo.Pk0102Header.Filedate = partInfo.Pk0304Header.Filedate;
            partInfo.Pk0102Header.Fnamelen = partInfo.Pk0304Header.Fnamelen;
            partInfo.Pk0102Header.Commentlen = 0;
            partInfo.Pk0102Header.Disknum = 0;
            partInfo.Pk0102Header.Inattr = 0;
            partInfo.Pk0102Header.Outattr = ShareMethodClass.IntToUInt((int)partInfo.FileAttribute);
            if (partInfo.Pk0304HeaderPos >= uint.MaxValue)
            {
                partInfo.Pk0102Header.Headerpos = uint.MaxValue;
            }
            else
            {
                partInfo.Pk0102Header.Headerpos = (uint)partInfo.Pk0304HeaderPos;
            }
            partInfo.Pk0102Header.filenameb = partInfo.Pk0304Header.filenameb;
            partInfo.Pk0102Header.Crc32 = partInfo.Pk0708Header.Crc32;
            partInfo.Pk0102Header.Uncompsize = partInfo.Pk0708Header.Uncompsize;
            partInfo.Pk0102Header.Compsize = partInfo.Pk0708Header.Compsize;
            partInfo.Pk0102Header.Comptype = partInfo.Pk0304Header.Comptype;

            // ExtraData
            List<byte> extraData = new List<byte>();
            if (partInfo.UnicodePathExtraData != null)
            {
                extraData.AddRange(partInfo.UnicodePathExtraData.GetBytes());
            }
            if (partInfo.AesExtraData != null)
            {
                extraData.AddRange(partInfo.AesExtraData.GetBytes());
            }
            if (partInfo.NtfsTimestampExtraData != null)
            {
                extraData.AddRange(partInfo.NtfsTimestampExtraData.GetBytes());
            }
            if (partInfo.ExtendedTimestampExtraData != null)
            {
                extraData.AddRange(partInfo.ExtendedTimestampExtraData.GetBytes(false));
            }
            if (partInfo.Zip64ExtraData != null)
            {
                extraData.AddRange(partInfo.Zip64ExtraData.GetCentralDirectoryBytes());
            }
            partInfo.Pk0102Header.extradatab = extraData.ToArray();
            partInfo.Pk0102Header.Extralen = (ushort)partInfo.Pk0102Header.extradatab.Length;
        }

        /// <summary>
        /// ローカルファイルヘッダ編集
        /// </summary>
        /// <param name="partInfo"></param>
        /// <remarks></remarks>
        private void EditPK0304(PartInfoClass partInfo)
        {
            partInfo.Pk0304Header = new ZipHeader.PK0304Info();
            partInfo.Pk0304Header.Signature = SIG_PK0304;

            // opt
            ushort opt = 0;
            if (partInfo.Password != null)
            {
                opt = (ushort)(opt | (ushort)HeaderGeneralFlag.ENCRYPTION);
            }
            if (ZipFileNameEncoding.CodePage == ShareMethodClass.Utf8Encoding.CodePage)
            {
                opt = (ushort)(opt | (ushort)HeaderGeneralFlag.UTF_ENCODING);
            }
            if (partInfo.WriteDataDescriptor)
            {
                opt = (ushort)(opt | (ushort)HeaderGeneralFlag.DESCRIPTION_PRESENT);
            }
            if(partInfo.CompressionOption == COMPRESSION_OPTION.DEFLATE)
            {
                switch (partInfo.CompressionLevel)
                {
                    case CompressionLevel.Optimal:
                        opt |= 0b000;
                        break;
                    case CompressionLevel.Fastest:
                        opt |= 0b100;
                        break;
                    case CompressionLevel.NoCompression:
                        opt |= 0b110;
                        break;
#if NET
                    case CompressionLevel.SmallestSize:
                        opt |= 0b010;
                        break;
#endif
                }
            }
            partInfo.Pk0304Header.Opt = opt;

            // comptype
            ushort comptype = 0;
            if (partInfo.CompressionOption == COMPRESSION_OPTION.DEFLATE)
            {
                comptype = (ushort)HeaderComptype.DEFLATE;
            }
            if (partInfo.AesExtraData != null)
            {
                comptype = (ushort)HeaderComptype.AES_ENCRYPTION;
            }
            partInfo.Pk0304Header.Comptype = comptype;

            // File Timestamp(DOS)
            partInfo.Pk0304Header.Filetime = ShareMethodClass.EncodeDosFileTime(partInfo.FileModifyTimeStamp);
            partInfo.Pk0304Header.Filedate = ShareMethodClass.EncodeDosFileDate(partInfo.FileModifyTimeStamp);

            // FileName
            partInfo.Pk0304Header.filenameb = partInfo.FileName;

            // FileName Length
            partInfo.Pk0304Header.Fnamelen = (ushort)partInfo.Pk0304Header.filenameb.Length;

            // DataDescriptorは現在常にOFF
            if (partInfo.WriteDataDescriptor)
            {
                partInfo.Pk0304Header.Crc32 = 0u;
                partInfo.Pk0304Header.Compsize = 0u;
                partInfo.Pk0304Header.Uncompsize = 0u;
            }
            else
            {
                partInfo.Pk0304Header.Crc32 = partInfo.Pk0708Header.Crc32;
                if (partInfo.Pk0708Header.CompsizeLong >= uint.MaxValue || partInfo.Pk0708Header.UncompsizeLong >= uint.MaxValue)
                {
                    partInfo.Pk0304Header.Compsize = uint.MaxValue;
                    partInfo.Pk0304Header.Uncompsize = uint.MaxValue;
                }
                else
                {
                    partInfo.Pk0304Header.Compsize = partInfo.Pk0708Header.Compsize;
                    partInfo.Pk0304Header.Uncompsize = partInfo.Pk0708Header.Uncompsize;
                }
            }

            // ExtraData
            List<byte> extraData = new List<byte>();
            if (partInfo.UnicodePathExtraData != null)
            {
                extraData.AddRange(partInfo.UnicodePathExtraData.GetBytes());
            }
            if (partInfo.AesExtraData != null)
            {
                extraData.AddRange(partInfo.AesExtraData.GetBytes());
            }
            if (partInfo.NtfsTimestampExtraData != null)
            {
                extraData.AddRange(partInfo.NtfsTimestampExtraData.GetBytes());
            }
            if (partInfo.ExtendedTimestampExtraData != null)
            {
                extraData.AddRange(partInfo.ExtendedTimestampExtraData.GetBytes(true));
            }
            if (partInfo.Zip64ExtraData != null && (partInfo.Zip64ExtraData.Compsize >= uint.MaxValue || partInfo.Zip64ExtraData.Uncompsize >= uint.MaxValue))
            {
                extraData.AddRange(partInfo.Zip64ExtraData.GetLocalHeaderBytes());
            }
            partInfo.Pk0304Header.extradatab = extraData.ToArray();
            partInfo.Pk0304Header.Extralen = (ushort)partInfo.Pk0304Header.extradatab.Length;
        
        }

        ///// <summary>
        ///// DataDescriptor編集
        ///// </summary>
        ///// <param name="partInfo"></param>
        ///// <param name="pk0708Work"></param>
        ///// <remarks></remarks>
        //private static void EditPK0708(PartInfoClass partInfo, ZipHeader.PK0708Info pk0708Work)
        //{
        //    partInfo.Pk0708Header = new ZipHeader.PK0708Info();
        //    partInfo.Pk0708Header.Signature = SIG_PK0708;
        //    partInfo.Pk0708Header.Crc32 = pk0708Work.Crc32;
        //    partInfo.Pk0708Header.CompsizeLong = pk0708Work.CompsizeLong;
        //    partInfo.Pk0708Header.UncompsizeLong = pk0708Work.UncompsizeLong;
        //    if (!partInfo.WriteDataDescriptor &&
        //        partInfo.Password != null)
        //    {
        //        switch (partInfo.EncryptionOption)
        //        {
        //            case ENCRYPTION_OPTION.TRADITIONAL:
        //                partInfo.Pk0708Header.CompsizeLong += 12L;
        //                break;
        //            case ENCRYPTION_OPTION.AES128:
        //            case ENCRYPTION_OPTION.AES192:
        //            case ENCRYPTION_OPTION.AES256:
        //                partInfo.Pk0708Header.CompsizeLong =
        //                    partInfo.Pk0708Header.CompsizeLong +
        //                    ZipAesCryptStream.AES_SALT_LENGTH[(int)partInfo.EncryptionOption] + 12L;
        //                partInfo.Pk0708Header.Crc32 = 0u;
        //                break;
        //        }
        //    }

        //}

        #endregion

        #endregion

        #region "非同期処理用"

        private readonly ConcurrentQueue<AsyncQueue> procQueueList = new ConcurrentQueue<AsyncQueue>();
        private readonly object writeBaseStreamLock = new object();
        private readonly object partInfoDicLock = new object();
        private Task writeStreamTask = Task.CompletedTask;
        /// <summary>キューに依頼順に登録するためのDictionary</summary>
        private readonly Dictionary<int, AsyncQueue> partInfoDic = new Dictionary<int, AsyncQueue>();
        private int partInfoDicCount = 0;
        /// <summary>PartInfoClassのID順にリストに追加するためのID。partInfo.Idは1から始まる</summary>
        private int addQueueId = 1;

        /// <summary>
        /// ベースストリームへの書き出しキュー追加
        /// </summary>
        /// <param name="partInfo"></param>
        /// <param name="stream"></param>
        /// <param name="abort"></param>
        /// <param name="cancelToken"></param>
        private void AddQueue(PartInfoClass partInfo, Stream stream, TaskAbort abort, CancellationToken cancelToken)
        {
            if (this.StoreInOrderAdded)
            {
                bool isAddQueue = false;
                lock (partInfoDicLock)
                {
                    // 辞書へ追加
                    partInfoDic.Add(partInfo.Id, new AsyncQueue(partInfo,stream));
                    // 依頼順にキューに追加
                    while (partInfoDic.ContainsKey(addQueueId))
                    {
                        procQueueList.Enqueue(partInfoDic[addQueueId]);
                        partInfoDic.Remove(addQueueId);
                        addQueueId++;
                        isAddQueue = true;
                    }
                    partInfoDicCount = partInfoDic.Count;
                }
                if (!isAddQueue)
                {
                    return;
                }
            }
            else
            {
                // 格納ファイルをキューに追加
                procQueueList.Enqueue(new AsyncQueue(partInfo, stream));
            }

#if DEBUG
            Stopwatch stp = Stopwatch.StartNew();
#endif

            // 書き込みタスクが起動していない場合は起動する
            lock (writeBaseStreamLock)
            {
                if (writeStreamTask.IsCompleted == true)
                {
                    if (writeStreamTask.Status != TaskStatus.RanToCompletion)
                    {
                        throw writeStreamTask.Exception;
                    }
                    writeStreamTask.Dispose();
                    writeStreamTask = WriteBaseStreamAsync(abort, cancelToken);
                }
            }

#if DEBUG
            Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : AddQueue() Elaps={stp.Elapsed.TotalMilliseconds}ms");
#endif

        }


        /// <summary>
        /// BASEストリームへの非同期書込み
        /// </summary>
        /// <returns></returns>
        private Task WriteBaseStreamAsync(TaskAbort abort, CancellationToken cancelToken)
        {
            return Task.Run(async () =>
            {
#if DEBUG
                Stopwatch stp = Stopwatch.StartNew();
                long writeCount = 0;
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : WriteBaseStreamAsync() start, queueCount={this.procQueueList.Count}");
#endif

                AsyncQueue queue = null;
                while (this.procQueueList.TryDequeue(out queue))
                {

                    try
                    {
                        // キャンセル監視
                        cancelToken.ThrowIfCancellationRequested();

                        abort.ThrowIfAbortRequested();

                        PartInfoClass partInfo = queue.PartInfo;

                        // ローカルヘッダの開始位置設定
                        partInfo.Pk0304HeaderPos = this.baseStream.Position;

                        // Zip64 ExtraData編集
                        EditZip64ExtraData(partInfo);

                        // PK0304編集(LocalHeader)
                        EditPK0304(partInfo);

                        // PK0102編集(CentralDirectory)
                        EditPK0102(partInfo);

                        // Needver設定
                        HeaderNeedver needver = HeaderNeedver.STORED;
                        if (partInfo.Zip64ExtraData != null)
                        {
                            // ZIP64
                            if (needver < HeaderNeedver.ZIP64)
                            {
                                needver = HeaderNeedver.ZIP64;
                            }
                        }
                        if ((partInfo.Pk0102Header.Outattr & (uint)FileAttributes.Directory) > 0 || string.IsNullOrWhiteSpace(Path.GetDirectoryName(partInfo.FullName)) == false)
                        {
                            // Directory
                            if (needver < HeaderNeedver.DIRECTORY)
                            {
                                needver = HeaderNeedver.DIRECTORY;
                            }
                        }
                        if ((partInfo.Pk0102Header.Comptype & (ushort)HeaderComptype.DEFLATE) > 0)
                        {
                            // Deflate
                            if (needver < HeaderNeedver.DEFLATE)
                            {
                                needver = HeaderNeedver.DEFLATE;
                            }
                        }
                        if ((partInfo.Pk0102Header.Opt & (ushort)HeaderGeneralFlag.ENCRYPTION) > 0)
                        {
                            // ZIP暗号化
                            if (needver < HeaderNeedver.ZIPENC)
                            {
                                needver = HeaderNeedver.ZIPENC;
                            }
                        }
                        if ((partInfo.Pk0102Header.Comptype & (ushort)HeaderComptype.AES_ENCRYPTION) > 0)
                        {
                            // AES暗号化
                            if (needver < HeaderNeedver.AESENC)
                            {
                                needver = HeaderNeedver.AESENC;
                            }
                        }
                        partInfo.Pk0102Header.Needver |= (ushort)needver;
                        partInfo.Pk0304Header.Needver |= (ushort)needver;

                        // PK0304出力
                        byte[] pk0304data = partInfo.Pk0304Header.GetBytes();
#if NET6_0_OR_GREATER
                        await this.baseStream.WriteAsync(new ReadOnlyMemory<byte>(pk0304data), cancelToken).ConfigureAwait(false);
#else
                        await this.baseStream.WriteAsync(pk0304data, 0, pk0304data.Length, cancelToken).ConfigureAwait(false);
#endif

                        // ファイルストリーム出力(ストリームがnullなのはディレクトリの場合)
                        if (queue.QueueStream != null)
                        {

                            // 圧縮/暗号化済のストリーム出力
                            await queue.QueueStream.CopyToAsync(baseStream, 81920, cancelToken).ConfigureAwait(false);
                            await baseStream.FlushAsync(cancelToken).ConfigureAwait(false);
                        }

                        partInfoList.Enqueue(partInfo);
                        this.ZipFileCount++;

                    }
                    catch
                    {
                        throw;
                    }
                    finally
                    {
                        try
                        {
                            // ストリーム破棄
                            queue.QueueStream?.Dispose();
                        }
                        catch { }
                    }

#if DEBUG
                    writeCount++;
#endif

                }

                //this.writeStreamExecute = false;

#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : WriteBaseStreamAsync() end, , writeCount={writeCount}, Elaps={stp.Elapsed.TotalMilliseconds}ms");
#endif

            }, cancelToken);
        }


        private class AsyncQueue
        {

            public PartInfoClass PartInfo { get; } = null;

            public Stream QueueStream { get; } = null;

            public AsyncQueue(PartInfoClass partInfo, Stream queueStream)
            {
                PartInfo = partInfo;
                QueueStream = queueStream;
            }
        }


#endregion

        #region "Dispose"

        private bool disposedValue;

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: マネージド状態を破棄します (マネージド オブジェクト)

#if DEBUG
                    //Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : Dispose()");
#endif

                    if(this.bytePassword != null)
                    {
                        // パスワードのバイト配列をクリア
                        if (this.bytePassword.Length > 0)
                        {
                            Array.Clear(this.bytePassword, 0, this.bytePassword.Length);
                        }
                    }   
                    this.bytePassword = null;

                    foreach (var queue in this.partInfoDic.Values)
                    {
                        try
                        {
                            queue.QueueStream?.Dispose();
                        }
                        catch { }
                    }   

                    foreach (var queue in this.procQueueList)
                    {
                        try
                        {
                            queue.QueueStream?.Dispose();
                        }
                        catch { }
                    }

                    // baseStream
                    try
                    {
                        baseStream?.Dispose();
                    }
                    catch (Exception) { }

                    try
                    {
                        dirDic?.Clear();
                    }
                    catch (Exception) { }

                    filenameEncoding = null;

                    try
                    {
                        baseFileStream?.Dispose();
                    }
                    catch (Exception) { }

                    try
                    {
                        addZipSemaphore?.Dispose();
                    }
                    catch (Exception) { }

                    tsm?.Dispose();

                }

                // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // TODO: 大きなフィールドを null に設定します
                disposedValue = true;
            }
        }

        // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
        // ~ZipArcClass()
        // {
        //     // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
        //     Dispose(disposing: false);
        // }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion

    }
}
