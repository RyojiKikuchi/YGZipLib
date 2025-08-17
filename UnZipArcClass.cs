using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Concurrent;
#if YGZIPLIB
using YGZipLib.Common;
using YGZipLib.Streams;
#elif YGMAILLIB
using YGMailLib.Zip.Common;
using YGMailLib.Zip.Streams;
#endif

#if YGZIPLIB
namespace YGZipLib
#elif YGMAILLIB
namespace YGMailLib.Zip
#endif
{
    /// <summary>
    /// ZIP書庫展開クラス
    /// </summary>
    /// <remarks></remarks>
    public class UnZipArcClass : IDisposable
	{

		#region "CONST"

		/// <summary>
		/// デフォルトの最大の多重度
		/// </summary>
		private const int MAX_SEMAPHORE_COUNT = 8;

        /// <summary>
        /// EndOfCentralDirectory(PK0506)検索用のバッファサイズ
        /// </summary>
		/// <remarks>ZIPコメント(65535)+EndOnCentralDirectory(22)+Omake(4)</remarks>
        private const int PK0506_SEARCH_BUF_SIZE = 65561;

        /// <summary>Compression Method</summary>
        internal enum HeaderComptype : UInt16
		{
			DEFLATE = 0x0008,
			DEFLATE64 = 0x0009,
			AES_ENCRYPTION = 0x0063
		}

        private const int READ_BUFFER_SIZE = 8192;
        private const int WRITE_BUFFER_SIZE = 32768;

        #endregion

        #region "メンバ変数"

        /// <summary>書庫内ファイルリスト</summary>
        private readonly List<CentralDirectoryInfo> centralDirectoryList = new List<CentralDirectoryInfo>();
		/// <summary>書庫およびパスワードのエンコーディング</summary>
		private Encoding defaultZipFileNameEncoding = ShareMethodClass.AnsiEncoding;
		/// <summary>パスワード付きファイルの復号に使用するパスワード</summary>
		private byte[] defaultPassword = null;

        /// <summary>ZIP書庫のストリーム</summary>
        private readonly UnzipTemp unzipTempStream = null;
        /// <summary>起動プロセス数制御のためのSemaphore</summary>
        private readonly SemaphoreSlim zipSemaphore = null;
        /// <summary>圧縮処理最大多重度</summary>
        private readonly int semaphoreCount;

        /// <summary>EndOfCentralDirectory(PK0506)の位置</summary>
        long pk0506pos = -1;
        /// <summary>EndOfCentralDirectory(PK0606)の位置</summary>
        long pk0606pos = -1;
        /// <summary>EndOfCentralDirectory(PK0506)情報</summary>
        ZipHeader.PK0506Info pk0506 = null;
        /// <summary>EndOfCentralDirectory(PK0606)情報</summary>
        ZipHeader.PK0606Info pk0606 = null;

        /// <summary>処理中のファイル</summary>
        private readonly ConcurrentDictionary<CentralDirectoryInfo, bool> processingFiles = new ConcurrentDictionary<CentralDirectoryInfo, bool>();

        #endregion

        #region "Properties"

        /// <summary>
        /// ファイル名・パスワードのエンコーディング  Default:システム規定値(<see cref="CultureInfo.CurrentUICulture"/>)
		/// <para>この設定に係わらずUTF-8で格納されたファイル名は正しく展開できる。<br />
        /// .NET Framework環境以外(例:.net Core)ではCodePagesEncodingProviderを登録しないとasciiとutf系しか利用できない。<br />
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
				return defaultZipFileNameEncoding;
			}
			set
			{
				defaultZipFileNameEncoding = value;
				foreach (CentralDirectoryInfo cdir in centralDirectoryList)
				{
					cdir.ZipFileNameEncoding = defaultZipFileNameEncoding;
				}
			}
		}

		/// <summary>
		/// 書庫の復号に使用するパスワード
		/// <para>暗号化ZIPの場合指定する</para>
		/// </summary>
		/// <value></value>
		/// <remarks></remarks>
		public string Password
		{
			set
			{
                if (defaultPassword != null && defaultPassword.Length > 0)
                    Array.Clear(defaultPassword, 0, defaultPassword.Length);
                defaultPassword = null;
                if (value == null)
                {
                    return;
                }
                byte[] p = ShareMethodClass.EncodingGetBytes(value, this.ZipFileNameEncoding);

                if (p.Length > 0xFFFF)
                {
                    throw new ArgumentException("Password length must be less than 65536 bytes.", nameof(value));
                }
                defaultPassword = p;
			}
		}

		/// <summary>
		/// 書庫内のファイル一覧を取得
		/// </summary>
		/// <value></value>
		/// <returns></returns>
		/// <remarks></remarks>
		public List<CentralDirectoryInfo> FileList
		{
			get
			{
				List<CentralDirectoryInfo> ret = new List<CentralDirectoryInfo>();
				centralDirectoryList.ForEach(c => ret.Add(c));
				return ret;
			}
		}

        /// <summary>
        /// 書庫内のファイル一覧を取得
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        /// <remarks></remarks>
        [Obsolete("Use FileList instead.", false)]
		[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public List<CentralDirectoryInfo> GetFileList => FileList;

        /// <summary>
        /// 実行中の展開プロセス数
        /// </summary>
        public int DecompressionProcs => semaphoreCount - zipSemaphore.CurrentCount;

        /// <summary>
        /// 処理中のファイルを返却<br />
        /// </summary>
        public string[] InProcessFilename
        {
            get
            {
                List<string> list = new List<string>();
                processingFiles.Keys.ToList().ForEach(cd => {
                    list.Add(cd.FullName);
                });
                return list.ToArray();
            }
        }

        /// <summary>
        /// コメント(bytes)
        /// </summary>
        public byte[] CommentBytes
        {
            get
            {
                if (pk0506.Commentlen > 0)
                {
                    return pk0506.Comment;
                }
                return default;
            }
        }

        /// <summary>
        /// コメント(string)
        /// </summary>
        public string Comment
        {
            get
            {
                if (pk0506.Commentlen > 0)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        foreach (byte c in pk0506.Comment)
                        {
                            if (c == 0)
                                break;
                            ms.WriteByte(c);
                        }
                        return ShareMethodClass.EncodingGetString(ms.ToArray(), ZipFileNameEncoding);
                    }
                }
                return default;
            }
        }

        #endregion

        #region "コンストラクタ"

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="zipStream">ZIP書庫ストリーム</param>
		/// <param name="tempDirPath">テンポラリファイルの作成ディレクトリ</param>
        /// <remarks></remarks>
        public UnZipArcClass(Stream zipStream, string tempDirPath = null)
		{
			semaphoreCount = GetSemaphoreCount();
			zipSemaphore = new SemaphoreSlim(semaphoreCount);
			unzipTempStream = new UnzipTemp(zipStream, tempDirPath);
            try
            {
                GetCentralDirectory();
            }
            catch
            {
                try { unzipTempStream?.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="zipByts">ZIP書庫のバイト配列</param>
        public UnZipArcClass(byte[] zipByts)
		{
			semaphoreCount = GetSemaphoreCount();
			zipSemaphore = new SemaphoreSlim(semaphoreCount);
			unzipTempStream = new UnzipTemp(zipByts);
            try
            {
                GetCentralDirectory();
            }
            catch
            {
                try { unzipTempStream?.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="zipFilePath">ZIP書庫のパス</param>
        public UnZipArcClass(string zipFilePath)
		{
			semaphoreCount = GetSemaphoreCount();
			zipSemaphore = new SemaphoreSlim(semaphoreCount);
			unzipTempStream = new UnzipTemp(zipFilePath);
            try
            {
                GetCentralDirectory();
            }
            catch
            {
                try { unzipTempStream?.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="zipStream">ZIP書庫ストリーム</param>
        /// <param name="semaphoreCount">圧縮処理最大多重度(0の場合は<see cref="P:System.Environment.ProcessorCount" />)</param>
        /// <param name="tempDirPath">テンポラリファイルの作成ディレクトリ</param>
        /// <remarks></remarks>
        public UnZipArcClass(Stream zipStream, int semaphoreCount, string tempDirPath = null)
		{
			this.semaphoreCount = (semaphoreCount <= 0) ? GetSemaphoreCount() : semaphoreCount;
			zipSemaphore = new SemaphoreSlim(this.semaphoreCount);
			unzipTempStream = new UnzipTemp(zipStream, tempDirPath);
			try
			{
                GetCentralDirectory();
			}
			catch
			{
				try { unzipTempStream?.Dispose(); } catch { }
				throw;
			}
		}

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="zipByts">ZIP書庫のバイト配列</param>
        /// <param name="semaphoreCount">圧縮処理最大多重度(0の場合は<see cref="P:System.Environment.ProcessorCount" />)</param>
        public UnZipArcClass(byte[] zipByts, int semaphoreCount)
        {
            this.semaphoreCount = (semaphoreCount <= 0) ? GetSemaphoreCount() : semaphoreCount;
			zipSemaphore = new SemaphoreSlim(this.semaphoreCount);
			unzipTempStream = new UnzipTemp(zipByts);
            try
            {
                GetCentralDirectory();
            }
            catch
            {
                try { unzipTempStream?.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="zipFilePath">ZIP書庫のパス</param>
        /// <param name="semaphoreCount">圧縮処理最大多重度(0の場合は<see cref="P:System.Environment.ProcessorCount" />)</param>
        public UnZipArcClass(string zipFilePath, int semaphoreCount)
		{
			this.semaphoreCount = (semaphoreCount <= 0) ? GetSemaphoreCount() : semaphoreCount;
			zipSemaphore = new SemaphoreSlim(this.semaphoreCount);
			unzipTempStream = new UnzipTemp(zipFilePath);
            try
            {
                GetCentralDirectory();
            }
            catch
            {
                try { unzipTempStream?.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// デフォルトの多重度取得
        /// </summary>
        /// <returns></returns>
        private static int GetSemaphoreCount()
		{
			int semCount = ((Environment.ProcessorCount >= MAX_SEMAPHORE_COUNT) ? MAX_SEMAPHORE_COUNT : Environment.ProcessorCount);
			if (semCount == 0)
			{
				return 1;
			}
			return semCount;
		}


		#endregion

		#region "CentralDirectoryInfo"

		/// <summary>
		/// CentralDirectory情報保持クラス
		/// </summary>
		/// <remarks></remarks>
		[Serializable]
		public class CentralDirectoryInfo
		{
			private readonly ZipHeader.PK0102Info centralDirectory = null;
			private Encoding defaultTextEncoding = null;
			private ushort compType;
			private bool compTypeOv = false;
			private Encoding utf8Encoding = null;
			private readonly UnzipTemp unzipTempStream = null;

			#region "Properties"

			/// <summary>
			/// CentralDirectoryの開始位置
			/// </summary>
			internal long Position { get; private set; } = -1;

            /// <summary>
            /// ローカルヘッダの開始位置
            /// </summary>
            /// <value></value>
            /// <returns></returns>
            /// <remarks></remarks>
            internal long LocalHeaderPos
			{
				get
				{
                    ZipHeader.Zip64ExtraDataInfo zip64Extended = GetZip64ExtraData();
                    if (zip64Extended == null)
                    {
                        return centralDirectory.Headerpos;
                    }
                    if (centralDirectory.Headerpos == uint.MaxValue)
                    {
                        return (long)zip64Extended.HeaderOffset;
                    }
                    return centralDirectory.Headerpos;
                }
            }

			private ZipHeader.PK0304Info _LocalHeader = null;

			internal ZipHeader.PK0102Info Pk0102 => this.centralDirectory;

            /// <summary>
            /// ローカルヘッダ取得
            /// </summary>
            internal ZipHeader.PK0304Info LocalHeader
            {
				get {
                    if (_LocalHeader == null)
                    {
						using(Stream st= unzipTempStream.GetZipStream())
						{
                            st.Position = this.LocalHeaderPos;
							try
							{
                                _LocalHeader = new ZipHeader.PK0304Info(st);
                            }
                            catch(Exception ex)
							{
								throw new InvalidDataException($"Local header not found. Name={this.FullName}, IsDirectory{this.IsDirectory}, LocalHeaderPos={this.LocalHeaderPos:X16}", ex);
							}
                        }
                    }
                    return _LocalHeader;
                }
            }

            /// <summary>CRC32</summary>
            public uint Crc32 => centralDirectory.Crc32;

			/// <summary>Flag</summary>
			internal ushort Flag => centralDirectory.Opt;

			/// <summary>Comptype</summary>
			internal ushort CompressionMethod => centralDirectory.Comptype;

			/// <summary>Comptype</summary>
			internal ushort CompressionMethodOv
			{
				get
				{
					if (compTypeOv)
					{
						return compType;
					}
					return centralDirectory.Comptype;
				}
				set
				{
					compType = value;
					compTypeOv = true;
				}
			}

			internal ushort Filedate => centralDirectory.Filedate;

			internal ushort Filetime => centralDirectory.Filetime;

			/// <summary>
			/// ZIPファイル名のEncoding
			/// UTF8のフラグがセットされていた場合はUTF8を使用する
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			internal Encoding ZipFileNameEncoding
			{
				get
				{
					if (UtfEncoding())
					{
						if (this.utf8Encoding == null)
						{
							this.utf8Encoding = ShareMethodClass.Utf8Encoding;
						}
                        return utf8Encoding;
					}
					return defaultTextEncoding;
				}
				set
				{
					defaultTextEncoding = value;
				}
			}

			/// <summary>
			/// ファイル名
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			public string FileName => Path.GetFileName(FullName);

			/// <summary>
			/// ディレクトリを含むファイル名
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			public string FullName
			{
				get
				{
                    string headerFileName = ShareMethodClass.EncodingGetString(centralDirectory.filenameb, ZipFileNameEncoding);
					ZipHeader.UnicodePathExtraDataField unicodePath = GetUnicodePathExtraData(headerFileName, centralDirectory.filenameb);
					if(unicodePath == null)
					{
                        if (centralDirectory.Fnamelen == 0)
                        {
                            return string.Empty;
                        }
                        return headerFileName;
                    }
					return unicodePath.UnicodeName;
                }
			}

			/// <summary>
			/// ディレクトリ名
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			public string DirectoryName => Path.GetDirectoryName(FullName);

			/// <summary>
			/// 作成タイムスタンプ
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			public DateTime CreateTimestamp
			{
				get
				{
                    // NTFSタイムスタンプ取得
                    try
                    {
                        ZipHeader.NtfsDateExtraDataInfo ntfsTimestamp = GetNtfsDataExtraField();
                        if (ntfsTimestamp != null)
                        {
                            if (ntfsTimestamp.CreateTime == 0)
                            {
                                return ModifyTimestamp;
                            }
                            return DateTime.FromFileTime(ntfsTimestamp.CreateTime);
                        }
                    }
                    catch { }
					// ExtendedTimestamp取得
                    try
                    {
						ZipHeader.ExtendedTimestampInfo extendedTimestamp = GetExtendedTimestamp();
						if (extendedTimestamp != null)
						{
							return extendedTimestamp.CreateTime;
						}
					}
					catch { }
                    // DOSタイムスタンプを設定
                    return ShareMethodClass.DecodeDosFileDateTime(centralDirectory.Filedate, centralDirectory.Filetime);
				}
			}

			/// <summary>
			/// 更新タイムスタンプ
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			public DateTime ModifyTimestamp
			{
				get
				{
                    // NTFSタイムスタンプ取得
                    try
                    {
                        ZipHeader.NtfsDateExtraDataInfo ntfsTimestamp = GetNtfsDataExtraField();
                        if (ntfsTimestamp != null)
                        {
                            return DateTime.FromFileTime(ntfsTimestamp.ModifyTime);
                        }
                    }
                    catch { }
                    // ExtendedTimestamp取得
                    try
                    {
                        ZipHeader.ExtendedTimestampInfo extendedTimestamp = GetExtendedTimestamp();
                        if (extendedTimestamp != null)
                        {
                            return extendedTimestamp.ModifyTime;
                        }
                    }
                    catch { }
                    // DOSタイムスタンプを設定
                    return ShareMethodClass.DecodeDosFileDateTime(centralDirectory.Filedate, centralDirectory.Filetime);
				}
			}

			/// <summary>
			/// アクセスタイムスタンプ
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			public DateTime AccessTimestamp
			{
				get
				{
                    // NTFSタイムスタンプ取得
                    try
                    {
                        ZipHeader.NtfsDateExtraDataInfo ntfsTimestamp = GetNtfsDataExtraField();
                        if (ntfsTimestamp != null)
                        {
							if(ntfsTimestamp.AccessTime == 0)
							{
								return ModifyTimestamp;
							}
                            return DateTime.FromFileTime(ntfsTimestamp.AccessTime);
                        }
                    }
                    catch { }
                    // ExtendedTimestamp取得
                    try
                    {
                        ZipHeader.ExtendedTimestampInfo extendedTimestamp = GetExtendedTimestamp();
                        if (extendedTimestamp != null)
                        {
                            return extendedTimestamp.AccessTime;
                        }
                    }
                    catch { }
                    // DOSタイムスタンプを設定
                    return ShareMethodClass.DecodeDosFileDateTime(centralDirectory.Filedate, centralDirectory.Filetime);
				}
			}

			/// <summary>
			/// 格納前のファイルサイズ
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			public long FileSize
			{
				get
				{
					ZipHeader.Zip64ExtraDataInfo zip64Extended = GetZip64ExtraData();
					if (zip64Extended == null)
					{
						return centralDirectory.Uncompsize;
					}
                    if (centralDirectory.Uncompsize == uint.MaxValue)
                    {
                        return (long)zip64Extended.Uncompsize;
                    }
                    return centralDirectory.Uncompsize;
                }
            }

			/// <summary>
			/// 格納後のファイルサイズ
			/// </summary>
			/// <value></value>
			/// <returns></returns>
			/// <remarks></remarks>
			public long CompFileSize
			{
				get
				{
                    ZipHeader.Zip64ExtraDataInfo zip64Extended = GetZip64ExtraData();
                    if (zip64Extended == null)
                    {
                        return centralDirectory.Compsize;
                    }
                    if (centralDirectory.Compsize == uint.MaxValue)
                    {
                        return (long)zip64Extended.Compsize;
                    }
                    return centralDirectory.Compsize;
                }
            }

			/// <summary>
			/// ディレクトリフラグ
			/// </summary>
			/// <value></value>
			/// <returns>ディレクトリの場合True</returns>
			/// <remarks></remarks>
			public bool IsDirectory
			{
				get
				{
					if ((this.centralDirectory.Outattr & (UInt32)FileAttributes.Directory) == (UInt32)FileAttributes.Directory)
					{
						return true;
					}
					else
					{
						return false;
					}
				}
			}

			/// <summary>
			/// 暗号化フラグ
			/// </summary>
			/// <value></value>
			/// <returns>暗号化されている場合True</returns>
			/// <remarks></remarks>
			public bool IsEncryption
			{
				get
				{
					if ((centralDirectory.Opt & (UInt16)ZipArcClass.HeaderGeneralFlag.ENCRYPTION) == 0)
					{
						return false;
					}
					else
					{
						return true;
					}
				}
			}

			/// <summary>
			/// ToString
			/// </summary>
			/// <returns></returns>
			public override string ToString()
			{
				return ToString(false);
			}

			/// <summary>
			/// ToString
			/// </summary>
			/// <param name="localHeader"></param>
			/// <returns></returns>
			public string ToString(bool localHeader)
			{
				StringBuilder cdInfo = new StringBuilder();
				cdInfo.AppendLine($"CentralDirectoryInfo");
				if (ExtraFields.TryGetValue(ZipHeader.ExtraDataId.UnicodePath, out byte[] _))
				{
                    cdInfo.AppendLine($" FullName:{ShareMethodClass.EncodingGetString(centralDirectory.filenameb, ZipFileNameEncoding)}");
                    cdInfo.AppendLine($" FullName(Utf8):{FullName}");
				}
				else
				{
                    cdInfo.AppendLine($" FullName:{FullName}");
                }
                cdInfo.AppendLine($" DirectoryName:{DirectoryName}");
				cdInfo.AppendLine($" FileName:{FileName}");
                if (centralDirectory.Uncompsize == uint.MaxValue || centralDirectory.Compsize == uint.MaxValue)
                {
                    cdInfo.AppendLine($" CompFileSize:{centralDirectory.Compsize:X8}({centralDirectory.Compsize:N0}) {CompFileSize:X16}({CompFileSize:N0})");
                    cdInfo.AppendLine($" FileSize:{centralDirectory.Uncompsize:X8}({centralDirectory.Uncompsize:N0}) {FileSize:X16}({FileSize:N0})");
                }
                else
				{
                    cdInfo.AppendLine($" CompFileSize:{CompFileSize:X16}({CompFileSize:N0})");
                    cdInfo.AppendLine($" FileSize:{FileSize:X16}({FileSize:N0})");
                }
                cdInfo.AppendLine($" IntAttr:{centralDirectory.Inattr:X4}");
                cdInfo.AppendLine($" ExtAttr:{centralDirectory.Outattr:X8}({ShareMethodClass.ExternalFileAttributesString(centralDirectory.Outattr)})");
                cdInfo.AppendLine($" GeneralFlg:{centralDirectory.Opt:X4}({ShareMethodClass.GeneralPurposeBitString(centralDirectory.Opt)})");
                cdInfo.AppendLine($" CRC32:{Crc32:X8}");
				cdInfo.AppendLine($" Comptype:{centralDirectory.Comptype:X4}({ShareMethodClass.CompTypeString(centralDirectory.Comptype, centralDirectory.Opt)})");
				cdInfo.AppendLine($" DiskNum:{centralDirectory.Disknum:X4}({centralDirectory.Disknum})");
				cdInfo.AppendLine($" MadeVer:{centralDirectory.Madever:X4}({centralDirectory.Madever})");
				cdInfo.AppendLine($" NeedVer:{centralDirectory.Needver:X4}({centralDirectory.Needver})");
				cdInfo.AppendLine($" Signature:{centralDirectory.Signature:X8}");
				cdInfo.AppendLine($" DosTimestamp:{ShareMethodClass.DecodeDosFileDateTime(Filedate, Filetime):yyyy/MM/dd HH:mm:ss.ff}");
				if (centralDirectory.Headerpos == uint.MaxValue)
				{
                    cdInfo.AppendLine($" LocalHeaderPos:{centralDirectory.Headerpos:X8}({centralDirectory.Headerpos:N0}) {LocalHeaderPos:X16}({LocalHeaderPos:N0})");
                }
                else
				{
                    cdInfo.AppendLine($" LocalHeaderPos:{LocalHeaderPos:X16}({LocalHeaderPos:N0})");
                }
                cdInfo.AppendLine($" CentralDirectoryPos:{Position:X16}({Position:N0})");
                foreach (KeyValuePair<ZipHeader.ExtraDataId, byte[]> ext in ExtraFields)
				{
					cdInfo.AppendLine($" ExtraField({(ushort)ext.Key:X4}):{BitConverter.ToString(ext.Value).Replace("-", string.Empty)}");
					using (MemoryStream ms = new MemoryStream(ext.Value, false))
					{
						using (InputStream ins = new InputStream(ms))
						{
                            switch (ext.Key)
                            {
                                case ZipHeader.ExtraDataId.Zip64ExtraData:
                                    ZipHeader.Zip64ExtraDataInfo ext0001 = new ZipHeader.Zip64ExtraDataInfo(ins,centralDirectory.Uncompsize,centralDirectory.Compsize,centralDirectory.Headerpos);
                                    cdInfo.AppendLine($"  * ZIP64 ExtraDataField");
                                    cdInfo.AppendLine($"  Uncompsize:{ext0001.Uncompsize:X16}({ext0001.Uncompsize:N0})");
                                    cdInfo.AppendLine($"  Compsize  :{ext0001.Compsize:X16}({ext0001.Compsize:N0})");
                                    cdInfo.AppendLine($"  Headeroffset:{ext0001.HeaderOffset:X16}({ext0001.HeaderOffset:N0})");
                                    cdInfo.AppendLine($"  Disknum:{ext0001.DiskNum:X8}({ext0001.DiskNum:X8})");
                                    break;
                                case ZipHeader.ExtraDataId.NtfsExtraData:
                                    ZipHeader.NtfsDateExtraDataInfo ext000a = new ZipHeader.NtfsDateExtraDataInfo(ins);
                                    cdInfo.AppendLine($"  * NTFS FileTimestamp ExtraDataField");
                                    cdInfo.AppendLine($"  CreateTimestamp:{ext000a.CreateTime:X16}({DateTime.FromFileTime(ext000a.CreateTime):yyyy/MM/dd HH:mm:ss.ff})");
                                    cdInfo.AppendLine($"  ModifyTimestamp:{ext000a.ModifyTime:X16}({DateTime.FromFileTime(ext000a.ModifyTime):yyyy/MM/dd HH:mm:ss.ff})");
                                    cdInfo.AppendLine($"  AccessTimestamp:{ext000a.AccessTime:X16}({DateTime.FromFileTime(ext000a.AccessTime):yyyy/MM/dd HH:mm:ss.ff})");
                                    break;
                                case ZipHeader.ExtraDataId.ExtendedTimestamp:
                                    ZipHeader.ExtendedTimestampInfo ext5455 = new ZipHeader.ExtendedTimestampInfo(ins);
                                    // local
                                    if (LocalExtraFields.ContainsKey(ZipHeader.ExtraDataId.ExtendedTimestamp) == true)
                                    {
                                        using (MemoryStream ms2 = new MemoryStream(LocalExtraFields[ZipHeader.ExtraDataId.ExtendedTimestamp]))
                                        {
                                            cdInfo.AppendLine(string.Format(" ExtraField({0:X4}):{1} (LocalHeader)", 0x5455u, BitConverter.ToString(LocalExtraFields[ZipHeader.ExtraDataId.ExtendedTimestamp])).Replace("-", string.Empty));
                                            ZipHeader.ExtendedTimestampInfo ext5455l = new ZipHeader.ExtendedTimestampInfo(ms2);
                                            cdInfo.AppendLine($"  * Extended Timestamp ExtraDataField(CentralDirectory / LocalHeader)");
                                            cdInfo.AppendLine($"  CreateTimestamp:{ext5455.CreateTime:yyyy/MM/dd HH:mm:ss.ff} / {ext5455l.CreateTime:yyyy/MM/dd HH:mm:ss.ff}");
                                            cdInfo.AppendLine($"  ModifyTimestamp:{ext5455.ModifyTime:yyyy/MM/dd HH:mm:ss.ff} / {ext5455l.ModifyTime:yyyy/MM/dd HH:mm:ss.ff}");
                                            cdInfo.AppendLine($"  AccessTimestamp:{ext5455.AccessTime:yyyy/MM/dd HH:mm:ss.ff} / {ext5455l.AccessTime:yyyy/MM/dd HH:mm:ss.ff}");
                                        }
                                    }
                                    else
                                    {
                                        cdInfo.AppendLine($"  * Extended Timestamp ExtraDataField");
                                        cdInfo.AppendLine($"  CreateTimestamp:{ext5455.CreateTime:yyyy/MM/dd HH:mm:ss.ff}");
                                        cdInfo.AppendLine($"  ModifyTimestamp:{ext5455.ModifyTime:yyyy/MM/dd HH:mm:ss.ff}");
                                        cdInfo.AppendLine($"  AccessTimestamp:{ext5455.AccessTime:yyyy/MM/dd HH:mm:ss.ff}");
                                    }
                                    break;
                                case ZipHeader.ExtraDataId.AesExtraData:
                                    ZipHeader.AesExtraDataInfo ext9901 = new ZipHeader.AesExtraDataInfo(ins);
                                    cdInfo.AppendLine($"  * AES ExtraDataField");
                                    cdInfo.AppendLine($"  VersionNumber:{ext9901.VersionNumber}");
                                    cdInfo.AppendLine($"  EncStrength:{ext9901.EncStrength}");
                                    cdInfo.AppendLine($"  comptype:{ext9901.Comptype:X4}({ShareMethodClass.CompTypeString(ext9901.Comptype, centralDirectory.Opt)})");
                                    break;
                                case ZipHeader.ExtraDataId.UnicodePath:
									ZipHeader.UnicodePathExtraDataField ext7075 = new ZipHeader.UnicodePathExtraDataField(ins, FullName, centralDirectory.filenameb);
                                    cdInfo.AppendLine($"  * UnicodePathExtraDataField");
                                    cdInfo.AppendLine($"  Version:{ext7075.Version}");
                                    cdInfo.AppendLine($"  NameCrc32:{ext7075.NameCrc32:X8}, HeaderNameCrc32:{ext7075.HeaderNameCrc32:X8}");
                                    cdInfo.AppendLine($"  UnicodeName:{ext7075.UnicodeNameOrg}");
                                    break;
                            }
                        }
                    }
				}
				if (centralDirectory.Commentlen > 0)
				{
					cdInfo.AppendLine($" Comment:{ShareMethodClass.EncodingGetString(centralDirectory.commentb, ZipFileNameEncoding)}");
				}
				if (localHeader)
				{
					cdInfo.AppendLine($", {this.LocalHeaderString}");
				}
				return cdInfo.ToString();
			}

			internal string LocalHeaderString
			{
				get
				{
					ZipHeader.PK0304Info localHeader = this.LocalHeader;
                    StringBuilder lcInfo = new StringBuilder();

                    lcInfo.AppendLine($"LocalHeaderInfo");
                    lcInfo.AppendLine($" FullName:{ShareMethodClass.EncodingGetString(LocalHeader.filenameb, ZipFileNameEncoding)}");

                    ZipHeader.Zip64ExtraDataInfo zip64Extended = null;
					if (localHeader.Uncompsize == uint.MaxValue || localHeader.Compsize == uint.MaxValue)
					{
						// Zip64ExtraDataFieldからファイルサイズを取得
						if (LocalExtraFields.TryGetValue(ZipHeader.ExtraDataId.Zip64ExtraData, out byte[] value))
						{
							ushort zip64ExtendedInfoLength = BitConverter.ToUInt16(value, 2);
							if (zip64ExtendedInfoLength >= 16)
							{
								using (MemoryStream ms = new MemoryStream(value, writable: false))
								{
									using (InputStream ins = new InputStream(ms))
									{
										zip64Extended = new ZipHeader.Zip64ExtraDataInfo(ins, localHeader.Uncompsize, localHeader.Compsize, 0);
									}
								}
							}
						}
					}
					if (zip64Extended == null) {
                        lcInfo.AppendLine($" CompFileSize:{localHeader.Compsize:X8}({localHeader.Compsize:N0})");
                        lcInfo.AppendLine($" FileSize:{localHeader.Uncompsize:X8}({localHeader.Uncompsize:N0})");
                    }
                    else
                    {
                        lcInfo.AppendLine($" CompFileSize:{localHeader.Compsize:X8}({localHeader.Compsize:N0}) {zip64Extended.Compsize:X16}({zip64Extended.Compsize:N0})");
                        lcInfo.AppendLine($" FileSize:{localHeader.Uncompsize:X8}({localHeader.Uncompsize:N0}) {zip64Extended.Uncompsize:X16}({zip64Extended.Uncompsize:N0})");
                    }

                    lcInfo.AppendLine($" GeneralFlg:{localHeader.Opt:X4}({ShareMethodClass.GeneralPurposeBitString(localHeader.Opt)})");
                    lcInfo.AppendLine($" CRC32:{localHeader.Crc32:X8}");
                    lcInfo.AppendLine($" Comptype:{localHeader.Comptype:X4}({ShareMethodClass.CompTypeString(localHeader.Comptype,localHeader.Opt)})");
                    lcInfo.AppendLine($" NeedVer:{localHeader.Needver:X4}({localHeader.Needver})");
					lcInfo.AppendLine($" Signature:{localHeader.Signature:X8}");
                    lcInfo.AppendLine($" DosTimestamp:{ShareMethodClass.DecodeDosFileDateTime(localHeader.Filedate, localHeader.Filetime):yyyy/MM/dd HH:mm:ss.ff}");

                    foreach (KeyValuePair<ZipHeader.ExtraDataId, byte[]> ext in this.LocalExtraFields)
                    {
                        lcInfo.AppendLine($" ExtraField({(ushort)ext.Key:X4}):{BitConverter.ToString(ext.Value).Replace("-", string.Empty)}");
                        using (MemoryStream ms = new MemoryStream(ext.Value, false))
                        {
                            using (InputStream ins = new InputStream(ms))
                            {
                                switch (ext.Key)
                                {
                                    case ZipHeader.ExtraDataId.Zip64ExtraData:
                                        ZipHeader.Zip64ExtraDataInfo ext0001 = new ZipHeader.Zip64ExtraDataInfo(ins, localHeader.Uncompsize, localHeader.Compsize, 0);
                                        lcInfo.AppendLine($"  * ZIP64 ExtraDataField");
                                        lcInfo.AppendLine($"  Uncompsize:{ext0001.Uncompsize:X16}({ext0001.Uncompsize:N0})");
                                        lcInfo.AppendLine($"  Compsize  :{ext0001.Compsize:X16}({ext0001.Compsize:N0})");
                                        lcInfo.AppendLine($"  Headeroffset:{ext0001.HeaderOffset:X16}({ext0001.HeaderOffset:N0})");
                                        lcInfo.AppendLine($"  Disknum:{ext0001.DiskNum:X8}({ext0001.DiskNum:X8})");
                                        break;
                                    case ZipHeader.ExtraDataId.NtfsExtraData:
                                        ZipHeader.NtfsDateExtraDataInfo ext000a = new ZipHeader.NtfsDateExtraDataInfo(ins);
                                        lcInfo.AppendLine($"  * NTFS FileTimestamp ExtraDataField");
                                        lcInfo.AppendLine($"  CreateTimestamp:{ext000a.CreateTime:X16}({DateTime.FromFileTime(ext000a.CreateTime):yyyy/MM/dd HH:mm:ss.ff})");
                                        lcInfo.AppendLine($"  ModifyTimestamp:{ext000a.ModifyTime:X16}({DateTime.FromFileTime(ext000a.ModifyTime):yyyy/MM/dd HH:mm:ss.ff})");
                                        lcInfo.AppendLine($"  AccessTimestamp:{ext000a.AccessTime:X16}({DateTime.FromFileTime(ext000a.AccessTime):yyyy/MM/dd HH:mm:ss.ff})");
                                        break;
                                    case ZipHeader.ExtraDataId.ExtendedTimestamp:
                                        ZipHeader.ExtendedTimestampInfo ext5455 = new ZipHeader.ExtendedTimestampInfo(ins);
                                        // local
                                        lcInfo.AppendLine($"  * Extended Timestamp ExtraDataField");
                                        lcInfo.AppendLine($"  CreateTimestamp:{ext5455.CreateTime:yyyy/MM/dd HH:mm:ss.ff}");
                                        lcInfo.AppendLine($"  ModifyTimestamp:{ext5455.ModifyTime:yyyy/MM/dd HH:mm:ss.ff}");
                                        lcInfo.AppendLine($"  AccessTimestamp:{ext5455.AccessTime:yyyy/MM/dd HH:mm:ss.ff}");
                                        break;
                                    case ZipHeader.ExtraDataId.AesExtraData:
                                        ZipHeader.AesExtraDataInfo ext9901 = new ZipHeader.AesExtraDataInfo(ins);
                                        lcInfo.AppendLine($"  * AES ExtraDataField");
                                        lcInfo.AppendLine($"  VersionNumber:{ext9901.VersionNumber}");
                                        lcInfo.AppendLine($"  EncStrength:{ext9901.EncStrength}");
                                        lcInfo.AppendLine($"  comptype:{ext9901.Comptype:X4}({ShareMethodClass.CompTypeString(ext9901.Comptype, localHeader.Opt)})");
                                        break;
                                    case ZipHeader.ExtraDataId.UnicodePath:
                                        ZipHeader.UnicodePathExtraDataField ext7075 = new ZipHeader.UnicodePathExtraDataField(ins, FullName, localHeader.filenameb);
                                        lcInfo.AppendLine($"  * UnicodePathExtraDataField");
                                        lcInfo.AppendLine($"  Version:{ext7075.Version}");
                                        lcInfo.AppendLine($"  NameCrc32:{ext7075.NameCrc32:X8}, HeaderNameCrc32:{ext7075.HeaderNameCrc32:X8}");
                                        lcInfo.AppendLine($"  UnicodeName:{ext7075.UnicodeNameOrg}");
                                        break;
                                }
                            }
                        }
                    }
                    return lcInfo.ToString();


                }
            }

            #endregion

            /// <summary>
            /// Constructor
            /// </summary>
			/// <param name="pos"></param>
            /// <param name="centralDirectory"></param>
            /// <param name="enc"></param>
			/// <param name="unzipTempStream"></param>
            internal CentralDirectoryInfo(long pos, ZipHeader.PK0102Info centralDirectory, Encoding enc, UnzipTemp unzipTempStream)
            {
				this.Position = pos;
				defaultTextEncoding = null;
				compTypeOv = false;
				this.centralDirectory = centralDirectory;
				ZipFileNameEncoding = enc;
				this.unzipTempStream = unzipTempStream;
			}

			/// <summary>
			/// 格納されているファイルのストリームを取得する
			/// </summary>
			/// <returns></returns>
			internal Stream GetStream()
			{
				Stream st = unzipTempStream.GetZipStream();
				st.Position = this.LocalHeaderPos;
				ZipHeader.PK0304Info pk0304 = new ZipHeader.PK0304Info(st);
				_LocalHeader = pk0304;
				return st;
            }

			/// <summary>
			/// ファイル名がUTF8エンコードされている
			/// </summary>
			/// <returns></returns>
			/// <remarks></remarks>
			private bool UtfEncoding()
			{
				if ((centralDirectory.Opt & 0x800u) != 0)
				{
					return true;
				}
				return false;
			}

			System.Collections.Generic.Dictionary<ZipHeader.ExtraDataId, byte[]> _ExtraFields = null;
            System.Collections.Generic.Dictionary<ZipHeader.ExtraDataId, byte[]> _LocalExtraFields = null;


            /// <summary>
            /// ExtraDataFieldを取得(CentralDirectory)
            /// </summary>
            internal Dictionary<ZipHeader.ExtraDataId, byte[]> ExtraFields
            {
                get
                {
                    if (_ExtraFields != null)
                    {
                        return _ExtraFields;
                    }
                    _ExtraFields = SplitExtraFields(centralDirectory.Extralen, centralDirectory.extradatab);
                    return _ExtraFields;
                }
            }

            /// <summary>
            /// ExtraDataFieldを取得(LocalHeader)
            /// </summary>
            internal Dictionary<ZipHeader.ExtraDataId, byte[]> LocalExtraFields
            {
                get
                {
                    if (_LocalExtraFields != null)
                    {
                        return _LocalExtraFields;
                    }
                    _LocalExtraFields = SplitExtraFields(LocalHeader.Extralen, LocalHeader.extradatab);
                    return _LocalExtraFields;
                }
            }

            private static Dictionary<ZipHeader.ExtraDataId, byte[]> SplitExtraFields(UInt16 extraLen,  byte[] extraFields)
            {
                Dictionary<ZipHeader.ExtraDataId, byte[]>  dic = new Dictionary<ZipHeader.ExtraDataId, byte[]>();
                int pos = 0;
                if (extraLen == 0)
                {
                    return dic;
                }
                do
                {
                    if (extraFields.Length - pos < 4)
                    {
                        // チェックエラー
                        return dic;
                    }
                    UInt16 header = BitConverter.ToUInt16(extraFields, pos);
                    UInt16 dataSize = BitConverter.ToUInt16(extraFields, pos + 2);
                    if (dataSize > extraFields.Length - pos - 4)
                    {
                        // チェックエラー
                        return dic;
                    }
                    List<byte> extraData = new List<byte>();
                    for (int i = 0; i < ((int)dataSize) + 4; i++)
                    {
                        extraData.Add(extraFields[pos + i]);
                    }
                    dic.Add((ZipHeader.ExtraDataId)header, extraData.ToArray());
                    pos += (int)dataSize + 4;
                }
                while (pos < extraLen);
                return dic;
            }

            /// <summary>
            /// NTFS時刻情報のExtraFieldを取得
            /// </summary>
            /// <returns></returns>
            /// <remarks></remarks>
            private ZipHeader.NtfsDateExtraDataInfo GetNtfsDataExtraField()
			{
				if (!ExtraFields.TryGetValue(ZipHeader.ExtraDataId.NtfsExtraData, out byte[] ntfsExtraData))
				{
					return null;
				}
				//byte[] ntfsExtraData = value;
				ushort ntfsExtraDataLength = BitConverter.ToUInt16(ntfsExtraData, 2);
				if ((uint)ntfsExtraDataLength < 32u)
				{
					return null;
				}
				ZipHeader.NtfsDateExtraDataInfo ntfsTimeStamp = null;
				using (MemoryStream ms = new MemoryStream(ntfsExtraData, writable: false))
				{
					using (InputStream ins = new InputStream(ms))
					{
						ntfsTimeStamp = new ZipHeader.NtfsDateExtraDataInfo(ins);
					}
				}
				return ntfsTimeStamp;
			}

			/// <summary>
			/// ExtendedTimestampの取得
			/// </summary>
			/// <returns></returns>
			private ZipHeader.ExtendedTimestampInfo GetExtendedTimestamp() {
				if (LocalExtraFields.ContainsKey(ZipHeader.ExtraDataId.ExtendedTimestamp) == true)
				{
                    using (MemoryStream ms = new MemoryStream(LocalExtraFields[ZipHeader.ExtraDataId.ExtendedTimestamp], false))
                    {
                        using (InputStream ins = new InputStream(ms))
                        {
                            return new ZipHeader.ExtendedTimestampInfo(ins);
                        }
                    }
                }
                if (ExtraFields.ContainsKey(ZipHeader.ExtraDataId.ExtendedTimestamp) == true)
                {
                    using (MemoryStream ms = new MemoryStream(ExtraFields[ZipHeader.ExtraDataId.ExtendedTimestamp], false))
                    {
                        using (InputStream ins = new InputStream(ms))
                        {
                            return new ZipHeader.ExtendedTimestampInfo(ins);
                        }
                    }
                }
                return null;
            }

            /// <summary>
            /// AES暗号化のExtraFieldを取得
            /// </summary>
            /// <returns></returns>
            /// <remarks></remarks>
            internal ZipHeader.AesExtraDataInfo GetAesExtraData()
			{
				if (!ExtraFields.TryGetValue(ZipHeader.ExtraDataId.AesExtraData, out byte[] value))
				{
					return null;
				}
				ushort aesExtraDataLength = BitConverter.ToUInt16(value, 2);
				if ((uint)aesExtraDataLength < 7u)
				{
					return null;
				}
				ZipHeader.AesExtraDataInfo aesExtraData = null;
				using (MemoryStream ms = new MemoryStream(value, writable: false))
				{
					using (InputStream ins = new InputStream(ms))
					{
						aesExtraData = new ZipHeader.AesExtraDataInfo(ins);
					}
				}
				if ((uint)aesExtraData.VersionNumber < 1u || (uint)aesExtraData.VersionNumber > 2u)
				{
					throw new CryptographicException($"AES Extra Data Field is broken. (version={aesExtraData.VersionNumber})");
				}
				if (aesExtraData.EncStrength < 1 || aesExtraData.EncStrength > 3)
				{
					throw new CryptographicException("AES Extra Data Field is broken.");
				}
				return aesExtraData;
			}

			/// <summary>
			/// ZIP64ExtraDataを取得
			/// </summary>
			/// <returns></returns>
			/// <remarks></remarks>
			private ZipHeader.Zip64ExtraDataInfo GetZip64ExtraData()
			{
				if (!ExtraFields.TryGetValue(ZipHeader.ExtraDataId.Zip64ExtraData, out byte[] value))
				{
					return null;
				}
				ushort zip64ExtendedInfoLength = BitConverter.ToUInt16(value, 2);
				if (zip64ExtendedInfoLength < 8)
				{
					return null;
				}
				ZipHeader.Zip64ExtraDataInfo zip64Extended;
				using (MemoryStream ms = new MemoryStream(value, writable: false))
				{
					using (InputStream ins = new InputStream(ms))
					{
						zip64Extended = new ZipHeader.Zip64ExtraDataInfo(ins,centralDirectory.Uncompsize,centralDirectory.Compsize,centralDirectory.Headerpos);
					}
				}
				return zip64Extended;
			}

			/// <summary>
			/// UnicodePathExtraDataを取得
			/// </summary>
			/// <param name="headerFileName"></param>
			/// <param name="headerFileNameb"></param>
			/// <returns></returns>
			private ZipHeader.UnicodePathExtraDataField GetUnicodePathExtraData(string headerFileName, byte[] headerFileNameb)
			{
                if (!ExtraFields.TryGetValue(ZipHeader.ExtraDataId.UnicodePath, out byte[] value))
                {
                    return null;
                }
                using (MemoryStream ms = new MemoryStream(value, writable: false))
				{
					return new ZipHeader.UnicodePathExtraDataField(ms, headerFileName, headerFileNameb);
				}
            }

		}

		#endregion

		#region "Public Method"

		#region "展開系"

		/// <summary>
		/// 1ファイル出力
		/// </summary>
		/// <param name="centralDirectory">CentoralDirectory</param>
		/// <param name="outFilePath">出力先ファイルのパス</param>
		public void PutFile(CentralDirectoryInfo centralDirectory, string outFilePath)
		{
            PutFileAsync(centralDirectory, new FileInfo(outFilePath), defaultPassword, TaskAbort.Create, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 1ファイル出力
        /// </summary>
        /// <param name="centralDirectory">CentoralDirectory</param>
        /// <param name="outFilePath">出力先ファイルのパス</param>
        /// <param name="password">パスワード</param>
        public void PutFile(CentralDirectoryInfo centralDirectory, string outFilePath, string password)
		{
			PutFileAsync(centralDirectory, outFilePath, password, CancellationToken.None).GetAwaiter().GetResult();
		}

		/// <summary>
		/// 1ファイル出力
		/// </summary>
		/// <param name="centralDirectory">CentoralDirectory</param>
		/// <param name="outFilePath">出力先ファイルのパス</param>
		/// <param name="cancelToken">キャンセルトークン</param>
		/// <returns></returns>
		public Task PutFileAsync(CentralDirectoryInfo centralDirectory, string outFilePath, CancellationToken cancelToken)
		{
            return PutFileAsync(centralDirectory, new FileInfo(outFilePath), defaultPassword, TaskAbort.Create, cancelToken);
        }

        /// <summary>
        /// 1ファイル出力
        /// </summary>
        /// <param name="centralDirectory">CentoralDirectory</param>
        /// <param name="outFilePath">出力先ファイルのパス</param>
        /// <param name="password">パスワード</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <returns></returns>
        public Task PutFileAsync(CentralDirectoryInfo centralDirectory, string outFilePath, string password, CancellationToken cancelToken)
		{
			return PutFileAsync(centralDirectory, new FileInfo(outFilePath), password, cancelToken);
		}

		/// <summary>
		/// ファイルの取得(ファイルへの出力)
		/// </summary>
		/// <param name="centralDirectory">CentralDirectory</param>
		/// <param name="fileInfo">出力先ファイルのFileInfo</param>
		/// <param name="password">暗号化パスワード</param>
		/// <param name="cancelToken"></param>
		/// <remarks></remarks>
		public Task PutFileAsync(CentralDirectoryInfo centralDirectory, FileInfo fileInfo, string password, CancellationToken cancelToken)
		{
            return PutFileAsync(centralDirectory, fileInfo, ShareMethodClass.EncodingGetBytes(password, this.ZipFileNameEncoding), TaskAbort.Create, cancelToken);
        }

        private Task PutFileAsync(CentralDirectoryInfo centralDirectory, FileInfo fileInfo, byte[] password,TaskAbort abort, CancellationToken cancelToken)
        {
			if (centralDirectory.IsEncryption == true)
			{
#if NET6_0_OR_GREATER
				ArgumentNullException.ThrowIfNull(password, nameof(password));
#else
				if (password == null) throw new ArgumentNullException(nameof(password));
#endif
            }
            if (centralDirectory.IsDirectory == true)
			{
				// ディレクトリは出力しない
				throw new ArgumentException("Cannot put directory file.", nameof(centralDirectory));
            }

            return Task.Run(async () =>
			{

				await this.zipSemaphore.WaitAsync(cancelToken).ConfigureAwait(false);

				try
				{

					processingFiles.TryAdd(centralDirectory, true);

					// タスクキャンセル判定
					cancelToken.ThrowIfCancellationRequested();

					abort.ThrowIfAbortRequested();

					// zip書庫のストリーム
					using (Stream zipStream = this.unzipTempStream.GetZipStream())
					{
						// ローカルヘッダに位置づけ
						zipStream.Position = centralDirectory.LocalHeaderPos;
						ZipHeader.PK0304Info localHeader = new ZipHeader.PK0304Info(zipStream);
						// 格納ファイルのデータ部分のみをInputStreamとして抽出
						using (InputStream compDataStream = new InputStream(zipStream, centralDirectory.CompFileSize))
						{
                            // 出力ファイル作成
                            using (FileStream outFs = new FileStream(fileInfo.FullName, FileMode.Create, FileAccess.Write, FileShare.None, WRITE_BUFFER_SIZE, FileOptions.SequentialScan | FileOptions.Asynchronous))
                            {
								// 展開処理
                                await DecryptionStreamAsync(centralDirectory, compDataStream, password, outFs, cancelToken).ConfigureAwait(false);
							}
						}
						// タイムスタンプ設定
						fileInfo.CreationTime = centralDirectory.CreateTimestamp;
						fileInfo.LastWriteTime = centralDirectory.ModifyTimestamp;
						fileInfo.LastAccessTime = centralDirectory.AccessTimestamp;

					}

				}
				catch (TaskAbort.TaskAbortException)
				{
					return;
				}
				catch (Exception ex)
				{
					abort.Abort();
					string cdInfo = string.Empty;
					try
					{
						cdInfo = centralDirectory.ToString();
					}
					catch { }
					throw new IOException($"IO error occurred. centralDirectory={cdInfo}", ex);
				}
				finally
				{
					this.zipSemaphore.Release();
					processingFiles.TryRemove(centralDirectory, out bool dmy);
				}

			}, cancelToken);

		}

		/// <summary>
		/// 1ファイル出力(ストリームに出力)
		/// </summary>
		/// <param name="centralDirectory">CentoralDirectory</param>
		/// <param name="outStream">出力先ストリーム</param>
		public void PutFile(CentralDirectoryInfo centralDirectory, Stream outStream)
		{
			PutFileAsync(centralDirectory, outStream, defaultPassword, TaskAbort.Create, CancellationToken.None).GetAwaiter().GetResult();
		}

		/// <summary>
		/// 1ファイル出力(ストリームに出力)
		/// </summary>
		/// <param name="centralDirectory">CentoralDirectory</param>
		/// <param name="outStream">出力先ストリーム</param>
		/// <param name="password">パスワード</param>
		public void PutFile(CentralDirectoryInfo centralDirectory, Stream outStream, string password)
		{
			PutFileAsync(centralDirectory, outStream, password, CancellationToken.None).GetAwaiter().GetResult();
		}

		/// <summary>
		/// 1ファイル出力(ストリームに出力)
		/// </summary>
		/// <param name="centralDirectory">CentralDirectory</param>
		/// <param name="outStream">出力先ストリーム</param>
		/// <param name="cancelToken">キャンセルトークン</param>
		/// <remarks></remarks>
		public Task PutFileAsync(CentralDirectoryInfo centralDirectory, Stream outStream, CancellationToken cancelToken)
		{
            return PutFileAsync(centralDirectory, outStream, defaultPassword, TaskAbort.Create, cancelToken);
        }

        /// <summary>
        /// 1ファイル出力(ストリームに出力)
        /// </summary>
        /// <param name="centralDirectory">CentralDirectory</param>
        /// <param name="outStream">出力先ストリーム</param>
        /// <param name="password">パスワード</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public Task PutFileAsync(CentralDirectoryInfo centralDirectory, Stream outStream, string password, CancellationToken cancelToken)
		{
            return PutFileAsync(centralDirectory, outStream, ShareMethodClass.EncodingGetBytes(password, this.ZipFileNameEncoding), TaskAbort.Create, cancelToken);
        }

        /// <summary>
        /// 1ファイル出力(ストリームに出力)
        /// </summary>
        /// <param name="centralDirectory">CentralDirectory</param>
        /// <param name="outStream">出力先ストリーム</param>
        /// <param name="password">パスワード</param>
		/// <param name="abort">タスクキャンセル用のAbortオブジェクト</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        private Task PutFileAsync(CentralDirectoryInfo centralDirectory, Stream outStream, byte[] password, TaskAbort abort, CancellationToken cancelToken)
        {
            // パスワードチェック
            if (centralDirectory.IsEncryption == true)
			{
                // 暗号化されているのにパスワードが指定されていない
#if NET6_0_OR_GREATER
				ArgumentNullException.ThrowIfNull(password, nameof(password));
#else
                if (password == null) throw new ArgumentNullException(nameof(password));
#endif

            }
            if (centralDirectory.IsDirectory == true)
            {
                // ディレクトリは出力しない
                throw new ArgumentException("Cannot put directory file.", nameof(centralDirectory));
            }

            return Task.Run(async () =>
			{

				await this.zipSemaphore.WaitAsync(cancelToken).ConfigureAwait(false);

				try
				{

					processingFiles.TryAdd(centralDirectory, false);

                    // タスクキャンセル判定
                    cancelToken.ThrowIfCancellationRequested();

                    abort.ThrowIfAbortRequested();

                    // zip書庫のストリーム
                    using (Stream zipStream = this.unzipTempStream.GetZipStream())
					{
						// ローカルヘッダに位置づけ
						zipStream.Position = centralDirectory.LocalHeaderPos;
						ZipHeader.PK0304Info localHeader = new ZipHeader.PK0304Info(zipStream);
						// 格納ファイルのデータ部分のみをInputStreamとして抽出
						using (InputStream compDataStream = new InputStream(zipStream, centralDirectory.CompFileSize))
						{
							// 展開処理
							await DecryptionStreamAsync(centralDirectory, compDataStream, password, outStream, cancelToken).ConfigureAwait(false);
						}
					}

				}
				catch (Exception ex)
				{
                    abort.Abort();
                    string cdInfo = string.Empty;
					try
					{
						cdInfo = centralDirectory.ToString();
					}
					catch { }
					throw new IOException($"IO error occurred. centralDirectory={cdInfo}", ex);
				}
				finally
				{
					this.zipSemaphore.Release();
					processingFiles.TryRemove(centralDirectory, out bool dmy);
				}

			}, cancelToken);

		}

		/// <summary>
		/// 指定したディレクトリに書庫を解凍する
		/// </summary>
		/// <param name="outputDirectory">出力先ディレクトリ(存在しない場合は作成する)</param>
		public void ExtractAllFiles(DirectoryInfo outputDirectory)
		{
			ExtractAllFilesPrivate(outputDirectory, defaultPassword);
		}

		/// <summary>
		/// 指定したディレクトリに書庫を解凍する
		/// </summary>
		/// <param name="outputDirectory">出力先ディレクトリ(存在しない場合は作成する)</param>
		/// <param name="password">パスワード</param>
		public void ExtractAllFiles(DirectoryInfo outputDirectory, string password)
		{
			ExtractAllFilesAsync(outputDirectory, password, CancellationToken.None).GetAwaiter().GetResult();
		}

		/// <summary>
		/// 指定したディレクトリに書庫を解凍する
		/// </summary>
		/// <param name="outputDirectory">出力先ディレクトリ</param>
		/// <param name="cancelToken">キャンセルトークン</param>
		/// <remarks></remarks>
		public Task ExtractAllFilesAsync(DirectoryInfo outputDirectory, CancellationToken cancelToken)
		{
			return ExtractAllFilesAsync(outputDirectory, defaultPassword, cancelToken);
		}

        /// <summary>
        /// 指定したディレクトリに書庫を解凍する
        /// </summary>
        /// <param name="outputDirectory">出力先ディレクトリ</param>
        /// <param name="password">暗号化パスワード</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        public Task ExtractAllFilesAsync(DirectoryInfo outputDirectory, string password, CancellationToken cancelToken)
		{
            return ExtractAllFilesAsync(outputDirectory, ShareMethodClass.EncodingGetBytes(password, this.ZipFileNameEncoding), cancelToken);
        }

        /// <summary>
        /// 指定したディレクトリに書庫を解凍する
        /// </summary>
        /// <param name="outputDirectory">出力先ディレクトリ(存在しない場合は作成する)</param>
        /// <param name="password">パスワード</param>
        private void ExtractAllFilesPrivate(DirectoryInfo outputDirectory, byte[] password)
        {
            ExtractAllFilesAsync(outputDirectory, password, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 指定したディレクトリに書庫を解凍する
        /// </summary>
        /// <param name="outputDirectory">出力先ディレクトリ</param>
        /// <param name="password">暗号化パスワード</param>
        /// <param name="cancelToken">キャンセルトークン</param>
        /// <remarks></remarks>
        private Task ExtractAllFilesAsync(DirectoryInfo outputDirectory, byte[] password, CancellationToken cancelToken)
		{

			if (outputDirectory.Exists == false)
			{
				outputDirectory.Create();
			}

			Dictionary<string, DirectoryInfo> dictionary = new Dictionary<string, DirectoryInfo>()
			{
				{
					string.Empty,
					outputDirectory
				}
			};

			List<Task> list = new List<Task>();

			TaskAbort abort = TaskAbort.Create;

			// ディレクトリの処理（ディレクトリは同期処理で一気に作ってしまう）
			centralDirectoryList.ToList().ForEach((centralDir) =>
			{
				if ((centralDir.IsDirectory || string.IsNullOrWhiteSpace(centralDir.DirectoryName) == false) && !dictionary.ContainsKey(centralDir.DirectoryName))
				{
					DirectoryInfo directoryInfo = new DirectoryInfo(CheckDirectory(outputDirectory, centralDir));
					if (!directoryInfo.Exists)
					{
						directoryInfo.Create();
					}
					dictionary.Add(centralDir.DirectoryName, directoryInfo);
				}
			});

			// ファイルの処理
			centralDirectoryList.ToList().ForEach((centralDir) =>
			{
				if (centralDir.IsDirectory == false)
				{
					string outFilePath = CheckFileName(dictionary[centralDir.DirectoryName], centralDir);
					list.Add(PutFileAsync(centralDir, new FileInfo(outFilePath), password, abort, cancelToken));
				}
			});

			if (list.Count == 0)
			{
				return Task.CompletedTask;
			}
			
			return Task.WhenAll(list);

		}

#endregion

		#region その他

#if DEBUG && YGZIPLIB

		/// <summary>
		/// CheckCentralDirectory
		/// </summary>
		/// <param name="centralDirectories"></param>
		/// <returns></returns>
        public string CheckCentralDirectory(List<CentralDirectoryInfo> centralDirectories)
        {
            return ShareMethodClass.CheckDirectories(this, centralDirectories);
		}

#endif

		#endregion

#endregion

		#region "ToString"

        /// <summary>
        /// ToString
        /// </summary>
        /// <returns></returns>
        public override string ToString()
		{
            StringBuilder zipInfo = new StringBuilder();
            zipInfo.AppendLine($"ZipFileInfo");
            zipInfo.AppendLine($" EndOfCentralDirectory Position:{pk0506pos:X16}({pk0506pos:N0})");
            zipInfo.AppendLine($"  EOD.Signature:{pk0506.Signature:X8}");
            zipInfo.AppendLine($"  EOD.Disknum:{pk0506.Disknum:X4}({pk0506.Disknum:N0})");
            zipInfo.AppendLine($"  EOD.Startdisknum:{pk0506.Startdisknum:X4}({pk0506.Startdisknum:N0})");
            zipInfo.AppendLine($"  EOD.Diskdirentry:{pk0506.Diskdirentry:X4}({pk0506.Diskdirentry:N0})");
            zipInfo.AppendLine($"  EOD.Direntry:{pk0506.Direntry:X4}({pk0506.Direntry:N0})");
            zipInfo.AppendLine($"  EOD.Dirsize:{pk0506.Dirsize:X8}({pk0506.Dirsize:N0})");
            zipInfo.AppendLine($"  EOD.Startpos:{pk0506.Startpos:X8}({pk0506.Startpos:N0})");
            zipInfo.AppendLine($"  EOD.Commentlen:{pk0506.Commentlen:N0}");
            if (pk0506.Commentlen > 0)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    foreach (byte c in pk0506.Comment)
                    {
                        if (c == 0)
                            break;
                        ms.WriteByte(c);
                    }
					zipInfo.AppendLine($"  EOD.Comment:{ShareMethodClass.EncodingGetString(ms.ToArray(), ZipFileNameEncoding)}");
                }
            }
            zipInfo.AppendLine($" Zip64EndOfCentralDirectory Position:{pk0606pos:X16}({pk0606pos:N0})");
            if (pk0606pos >= 0)
            {
                zipInfo.AppendLine($"  EOD64.Signature:{pk0606.Signature:X8}");
                zipInfo.AppendLine($"  EOD64.Madever:{pk0606.Madever:X4}({pk0606.Madever})");
                zipInfo.AppendLine($"  EOD64.Needver:{pk0606.Needver:X4}({pk0606.Needver})");
                zipInfo.AppendLine($"  EOD64.Disknum:{pk0606.Disknum:X8}({pk0606.Disknum:N0})");
                zipInfo.AppendLine($"  EOD64.Startdisknum:{pk0606.Startdisknum:X8}({pk0606.Startdisknum:N0})");
                zipInfo.AppendLine($"  EOD64.Diskdirentry:{pk0606.Diskdirentry:X16}({pk0606.Diskdirentry:N0})");
                zipInfo.AppendLine($"  EOD64.Direntry:{pk0606.Direntry:X16}({pk0606.Direntry:N0})");
                zipInfo.AppendLine($"  EOD64.Dirsize:{pk0606.Dirsize:X16}({pk0606.Dirsize:N0})");
                zipInfo.AppendLine($"  EOD64.Startpos:{pk0606.Startpos:X16}({pk0606.Startpos:N0})");
                if (pk0606.zip64extdatab != null)
                {
                    zipInfo.AppendLine(string.Format("  EOD64.Extdata:{0}", BitConverter.ToString(pk0606.zip64extdatab).Replace("-", string.Empty)));
                }
            }
            zipInfo.AppendLine($" CentralDirectoryCount:{centralDirectoryList.Count:N0}");
            return zipInfo.ToString();
        }

		#endregion

		#region "Private Methods"

        /// <summary>
        /// 出力先ディレクトリの妥当性チェック
        /// </summary>
        /// <param name="outputDirectory"></param>
        /// <param name="centralDirectory"></param>
        /// <remarks></remarks>
        private static string CheckDirectory(DirectoryInfo outputDirectory, CentralDirectoryInfo centralDirectory)
		{
            // 出力先ディレクトリチェック
#if NET6_0_OR_GREATER
			ArgumentNullException.ThrowIfNull(outputDirectory);
#else
            if (outputDirectory == null) throw new ArgumentNullException(nameof(outputDirectory));
#endif
            if (outputDirectory.Exists == false)
			{
				throw new DirectoryNotFoundException($"Output directory does not exist: {outputDirectory.FullName}");
            }

            // ディレクトリ名未指定
            if (string.IsNullOrEmpty(centralDirectory.DirectoryName))
            {
                throw new IOException($"InvalidDirectoryName:{centralDirectory}");
			}

            // アーカイブ内の相対ディレクトリ名を不正文字置換
            string sanitizedRelative = ShareMethodClass.ReplaceInvPathChar(centralDirectory.DirectoryName);

            // 絶対パス指定は拒否（例: "C:\..." や "/..."）
            if (Path.IsPathRooted(sanitizedRelative))
            {
                throw new IOException($"InvalidDirectoryName(absolute path):{centralDirectory}");
            }

            // 出力先の絶対パス（正規化）
            string baseFullPath = Path.GetFullPath(outputDirectory.FullName);

            // 結合して正規化
            string candidateFullPath = Path.GetFullPath(Path.Combine(baseFullPath, sanitizedRelative));

            // 厳密な前方一致のため、末尾に区切りを付与して比較
            string baseWithSep = baseFullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? baseFullPath
                : baseFullPath + Path.DirectorySeparatorChar;

            // OS毎の既定比較（Windowsは大小無視、他は大小区別）
            StringComparison cmp =
                (Environment.OSVersion.Platform == PlatformID.Win32NT ||
                 Environment.OSVersion.Platform == PlatformID.Win32S ||
                 Environment.OSVersion.Platform == PlatformID.Win32Windows ||
                 Environment.OSVersion.Platform == PlatformID.WinCE)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // 出力先ディレクトリ配下であることを検証（C:\out と C:\out2 の誤一致を防止）
            if (!candidateFullPath.StartsWith(baseWithSep, cmp))
            {
                throw new IOException($"InvalidDirectoryName:OutputFile={candidateFullPath},OutputDir={baseFullPath},{centralDirectory}");
            }

            return candidateFullPath;
        }

        private static string CheckFileName(DirectoryInfo outputDirectory, CentralDirectoryInfo centralDirectory)
		{
			if (string.IsNullOrEmpty(centralDirectory.FileName))
			{
				throw new IOException($"InvalidFileName:{centralDirectory}");
			}
            string workFile = Path.GetFullPath(Path.Combine(outputDirectory.FullName, ShareMethodClass.ReplaceInvFileChar(centralDirectory.FileName)));

            if (Path.GetDirectoryName(workFile).Equals(outputDirectory.FullName, StringComparison.OrdinalIgnoreCase) == false)
			{
				throw new IOException($"InvalidFileName:OutputFile={workFile},OutputDir={outputDirectory.FullName},{centralDirectory}");
			}
			return workFile;
		}

		/// <summary>
		/// 復号化ストリーム設定
		/// </summary>
		/// <param name="centralDirectory"></param>
		/// <param name="compDataStream"></param>
		/// <param name="password"></param>
		/// <param name="outputStream"></param>
		/// <param name="cancelToken"></param>
		/// <remarks></remarks>
		private static async Task DecryptionStreamAsync(CentralDirectoryInfo centralDirectory, 
			                                            InputStream compDataStream, 
														byte[] password, 
														Stream outputStream, 
														CancellationToken cancelToken)
		{
			if(centralDirectory.IsEncryption == true)
            {
                // 暗号化あり
                switch (centralDirectory.CompressionMethod)
                {
					case (UInt16)ZipArcClass.HeaderComptype.AES_ENCRYPTION:
						// AES復号設定
						ZipHeader.AesExtraDataInfo aesExtraData = centralDirectory.GetAesExtraData() ?? 
							throw new System.Security.Cryptography.CryptographicException("Bad ZIP file. (Unable to get Aes Extra Data Field)");
						centralDirectory.CompressionMethodOv = aesExtraData.Comptype;
						using (ZipAesCryptStream decryptStream = new ZipAesCryptStream(compDataStream, (ZipArcClass.ENCRYPTION_OPTION)aesExtraData.EncStrength, password, ZipAesCryptStream.StreamMode.DECRYPT, centralDirectory.CompFileSize)) 
						{
							await DecompressStreamAsync(centralDirectory, decryptStream, outputStream, cancelToken).ConfigureAwait(false);
						}
						break;
					default:
						// AES暗号化じゃなければ TRADITIONAL PKWARE Encryption と仮定する
						UInt32 checkCrc;
						if((centralDirectory.Flag & (UInt16)ZipArcClass.HeaderGeneralFlag.DESCRIPTION_PRESENT) == 0)
                        {
							checkCrc = centralDirectory.Crc32;
                        }
                        else
                        {
							checkCrc = (UInt32)centralDirectory.Filetime << 16;
						}
						using (ZipCryptStream decryptStream = new ZipCryptStream(compDataStream, password, checkCrc, ZipCryptStream.StreamMode.DECRYPT))
						{
							await DecompressStreamAsync(centralDirectory, decryptStream, outputStream, cancelToken).ConfigureAwait(false);
						}
						break;
                }
            }
            else
            {
				// 暗号化無し
				await DecompressStreamAsync(centralDirectory, compDataStream, outputStream, cancelToken).ConfigureAwait(false);
			}


		}

		/// <summary>
		/// 展開ストリーム設定
		/// </summary>
		/// <param name="centralDirectory"></param>
		/// <param name="compDataStream"></param>
		/// <param name="outputStream"></param>
		/// <param name="cancelToken"></param>
		/// <remarks></remarks>
		private static async Task DecompressStreamAsync(CentralDirectoryInfo centralDirectory, Stream compDataStream, Stream outputStream, CancellationToken cancelToken)
		{
            switch (centralDirectory.CompressionMethodOv)
            {
				case (UInt16)0u:
					// 無圧縮
					await StreamWriteAsync(centralDirectory, compDataStream, outputStream, cancelToken).ConfigureAwait(false);
					break;
                case (UInt16)HeaderComptype.DEFLATE:
                    // Deflate
                    using (DeflateStream decompressStream = new DeflateStream(compDataStream, CompressionMode.Decompress))
					{
						await StreamWriteAsync(centralDirectory, decompressStream, outputStream, cancelToken).ConfigureAwait(false);
					}
					break;
#if DEFLATE64
				case (UInt16)HeaderComptype.DEFLATE64:
					// Deflate64
					using (Deflate64Stream decompressStream = new Deflate64Stream(compDataStream, CompressionMode.Decompress))
					{
						await StreamWriteAsync(centralDirectory, decompressStream, outputStream, cancelToken).ConfigureAwait(false);
					}
					break;
#endif
				default:
					// 対応していない圧縮方式
					throw new System.IO.InvalidDataException($"Unknown Compression Method. filename={centralDirectory.FullName}, comp method={centralDirectory.CompressionMethodOv:X}");
			}

		}

		/// <summary>
		/// ストリーム出力
		/// </summary>
		/// <param name="centralDirectory"></param>
		/// <param name="readStream"></param>
		/// <param name="outputStream"></param>
		/// <param name="cancelToken"></param>
		/// <remarks></remarks>
		private static async Task StreamWriteAsync(CentralDirectoryInfo centralDirectory, Stream readStream, Stream outputStream, CancellationToken cancelToken)
		{
			UInt32 crc32 = 0;
			long outputCount = 0;
			// 出力ストリームに出力する
			using(CalcCrc32Stream crc32Stream = new CalcCrc32Stream(outputStream, CalcCrc32Stream.StreamMode.WRITE))
            {
				await readStream.CopyToAsync(crc32Stream, 81920, cancelToken).ConfigureAwait(false);
				outputCount = crc32Stream.IoCount;
				crc32 = crc32Stream.Crc32;
			}

			// サイズチェック
			if(centralDirectory.FileSize != outputCount)
            {
				// CentralDirectoryの圧縮前サイズとストリームに出力したファイルサイズが異なる
				throw new System.IO.InvalidDataException($"Bad ZIP file. (File size. filename={centralDirectory.FullName}, zip size={centralDirectory.FileSize}, unzip size={outputCount})");
			}

			// CRCチェック
			if(centralDirectory.IsEncryption == true &&
			   centralDirectory.CompressionMethod == (UInt16)ZipArcClass.HeaderComptype.AES_ENCRYPTION &&
			   centralDirectory.GetAesExtraData().VersionNumber == 2)
            {
                // CRCチェック対象外
                // AES暗号化でAE-2形式の場合、CRCが記録されていないためチェック対象外とする
            }
            else
            {
				if(centralDirectory.Crc32 != crc32)
                {
					// CRCアンマッチ
					throw new System.IO.InvalidDataException($"Bad ZIP file. (Crc Error. filename={centralDirectory.FullName}, zip crc={centralDirectory.Crc32:X}, unzip crc={crc32:X})");
				}
            }

		}

		/// <summary>
		/// CentralDirectoryを取得してリストに格納
		/// </summary>
		/// <remarks></remarks>
		private void GetCentralDirectory()
		{
			using (Stream zipStream = unzipTempStream.GetZipStream())
			{

#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetCentralDirectory : FindCentralDirectory Start");
                Stopwatch stp = Stopwatch.StartNew();
#endif

                // EndOfCentralDirectory(PK0506)の位置を取得
                pk0506pos = FindEndOfCentralDirectory(zipStream);
				// Zip64 end of central directory locatorの存在チェック
				pk0606pos = FindZip64EndOfCentralDirectory(pk0506pos, zipStream);

                // CentralDirectoryの開始位置と件数を取得
                long centralDirectoryStartPos;
				long centralDirectoryCount;
				long centralDirectoryLength;

				// pk0506取得
                zipStream.Position = pk0506pos;
                pk0506 = new ZipHeader.PK0506Info(zipStream);

                if (pk0606pos < 0)
				{
					// Zip64無し
					centralDirectoryStartPos = pk0506.Startpos;
					centralDirectoryCount = pk0506.Diskdirentry;
					centralDirectoryLength = pk0506.Dirsize;
				}
				else
				{
					// Zip64有り
					zipStream.Position = pk0606pos;
					pk0606 = new ZipHeader.PK0606Info(zipStream);
					centralDirectoryStartPos = (long)pk0606.Startpos;
					centralDirectoryCount = (long)pk0606.Diskdirentry;
					centralDirectoryLength = (long)pk0606.Dirsize;
				}

#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetCentralDirectory : FindCentralDirectory End, elaps={stp.Elapsed.TotalMilliseconds}ms");
                stp.Restart();
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetCentralDirectory : CreateMemoryStream Start");
#endif

				//byte[] centralDirestoryBytes= new byte[centralDirectoryLength];
				zipStream.Position = centralDirectoryStartPos;
                //ShareMethodClass.StreamReadBuffer(zipStream, centralDirestoryBytes);

#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetCentralDirectory : CreateMemoryStream End, elaps={stp.Elapsed.TotalMilliseconds}ms");
                stp.Restart();
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetCentralDirectory : GetCentralDirectory Start");
#endif

                // CentralDirectory(PK0102)とLocalHeader(PK0304)取得

                //using (MemoryStream centralDirectoryStream = new MemoryStream(centralDirestoryBytes, false))
                using (InputStream centralDirectoryStream = new InputStream(zipStream,centralDirectoryLength))
                {
                    for (long i = 0L; i < centralDirectoryCount; i++)
					{
						long pos = centralDirectoryStartPos + centralDirectoryStream.Position;
						ZipHeader.PK0102Info pk0102 = new ZipHeader.PK0102Info(centralDirectoryStream);
						CentralDirectoryInfo centralDirectory = new CentralDirectoryInfo(pos, pk0102, ZipFileNameEncoding, unzipTempStream);
						centralDirectoryList.Add(centralDirectory);
					}
				}

#if DEBUG
                Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetCentralDirectory : GetCentralDirectory End, elaps={stp.Elapsed.TotalMilliseconds}ms");
				//stp.Restart();
				//Debug.WriteLine($"Task {Task.CurrentId:x4}, ThreadId={Environment.CurrentManagedThreadId:x4} : GetCentralDirectory : GetLocalHeader Start");
#endif

			}
		}

		/// <summary>
		/// EndOfCentralDirectory(PK0506)の位置を取得
		/// </summary>
		/// <returns></returns>
		/// <remarks></remarks>
		private static long FindEndOfCentralDirectory(Stream zipStream)
		{
			int bufSize = (zipStream.Length > PK0506_SEARCH_BUF_SIZE) ? PK0506_SEARCH_BUF_SIZE : (int)zipStream.Length;
            zipStream.Position = zipStream.Length - bufSize;
			byte[] buf= new byte[bufSize];
			zipStream.Read(buf, 0, buf.Length);
            
            int startPos = bufSize - (new ZipHeader.PK0506Info().GetBytes().Length - 4) - 1;
			for(int i = startPos; i >= 0; i--)
			{
                if (BitConverter.ToUInt32(buf, i) == ZipArcClass.SIG_PK0506)
                {
                    return i + zipStream.Length - bufSize;
                }
			}
            throw new InvalidDataException("Bad ZIP file. (end of central directory record not found)");
        }

		/// <summary>
		/// Zip64 end of central directory recordの位置を取得
		/// </summary>
		/// <param name="pk0506pos"></param>
		/// <param name="zipStream"></param>
		/// <returns></returns>
		/// <remarks></remarks>
		private static long FindZip64EndOfCentralDirectory(long pk0506pos, Stream zipStream)
		{
			// Zip64 end of central directory locatorの位置を算出
			long locatorPos = pk0506pos - new ZipHeader.PK0607Info().GetBytes().Length;
			if (locatorPos < 0)
			{
				return -1L;
			}

			// Zip64 end of central directory locatorのシグネチャ取得
			zipStream.Position = locatorPos;
			byte[] buffer = new byte[4];
			int readLength = zipStream.Read(buffer, 0, buffer.Length);
			if(readLength < 4)
			{
				return -1L;
            }
            UInt32 locatorSignature = BitConverter.ToUInt32(buffer, 0);
			if (locatorSignature != ZipArcClass.SIG_PK0607)
			{
				return -1L;
			}

			// Zip64 end of central directory locator取得
			zipStream.Seek(-4L, SeekOrigin.Current);
			ZipHeader.PK0607Info pk607 = new ZipHeader.PK0607Info(zipStream);
			if (pk607.Startpos > (ulong)locatorPos)
			{
				return -1L;
			}

			// Zip64 end of central directory recordのシグネチャ取得
			zipStream.Position = (long)pk607.Startpos;
			zipStream.Read(buffer, 0, buffer.Length);
			UInt32 zip64ecdSignature = BitConverter.ToUInt32(buffer, 0);
			if (zip64ecdSignature != ZipArcClass.SIG_PK0606)
			{
				return -1L;
			}

			zipStream.Seek(-4L, SeekOrigin.Current);
			return zipStream.Position;
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

                    unzipTempStream?.Dispose();
                    defaultZipFileNameEncoding = null;
					if(defaultPassword != null)
					{
                        // パスワードのバイト配列をクリア
                        if (this.defaultPassword.Length > 0)
                        {
                            Array.Clear(this.defaultPassword, 0, this.defaultPassword.Length);
                        }
                    }
                    defaultPassword = null;
					zipSemaphore?.Dispose();

                }

                // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // TODO: 大きなフィールドを null に設定します
                centralDirectoryList?.Clear();

                disposedValue = true;
			}
		}

		// // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
		// ~UnZipArcClass()
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
