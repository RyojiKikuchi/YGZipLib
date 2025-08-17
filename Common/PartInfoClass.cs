using System;
using System.IO;
using System.IO.Compression;

#if YGZIPLIB
namespace YGZipLib.Common
#elif YGMAILLIB
namespace YGMailLib.Zip.Common
#endif
{

    /// <summary>PartInfoClass</summary>
    internal class PartInfoClass
	{

        /// <summary>ID</summary>
        public int Id { get; set; }

        /// <summary>ファイル名</summary>
        public byte[] FileName { get; set; } = null;

		/// <summary>格納ファイルの作成タイムスタンプ</summary>
		public DateTime FileCreateTimeStamp { get; set; } = DateTime.MinValue;

		/// <summary>格納ファイルの更新タイムスタンプ</summary>
		public DateTime FileModifyTimeStamp { get; set; } = DateTime.MinValue;

		/// <summary>格納ファイルのアクセスタイムスタンプ</summary>
		public DateTime FileAccessTimeStamp { get; set; } = DateTime.MinValue;

		/// <summary>ファイル属性</summary>
		public FileAttributes FileAttribute { get; set; } = 0;

		/// <summary>圧縮オプション</summary>
		public ZipArcClass.COMPRESSION_OPTION CompressionOption { get; set; } = ZipArcClass.COMPRESSION_OPTION.DEFLATE;

        /// <summary>圧縮レベル</summary>
        public System.IO.Compression.CompressionLevel CompressionLevel { get; set; } =System.IO.Compression.CompressionLevel.Optimal;

		/// <summary>暗号化オプション</summary>
		public ZipArcClass.ENCRYPTION_OPTION EncryptionOption { get; set; } = ZipArcClass.ENCRYPTION_OPTION.TRADITIONAL;

		/// <summary>暗号化パスワード</summary>
		public byte[] Password { get; set; } = null;

		/// <summary>PK0102Header</summary>
		public ZipHeader.PK0102Info Pk0102Header { get; set; } = null;

		/// <summary>PK0304Header位置</summary>
		public long Pk0304HeaderPos { get; set; } = 0;

		/// <summary>PK0304Header</summary>
		public ZipHeader.PK0304Info Pk0304Header { get; set; } = null;

		/// <summary>PK0708Header</summary>
		public ZipHeader.PK0708Info Pk0708Header { get; set; } = null;

		/// <summary>NTFS TimeStamp ExtraData</summary>
		public ZipHeader.NtfsDateExtraDataInfo NtfsTimestampExtraData { get; set; } = null;

        /// <summary>Extended TimeStamp ExtraData</summary>
        public ZipHeader.ExtendedTimestampInfo ExtendedTimestampExtraData { get; set; } = null;

        /// <summary>AES ExtraData</summary>
        public ZipHeader.AesExtraDataInfo AesExtraData { get; set; } = null;

		/// <summary>Zip64 ExtraData</summary>
		public ZipHeader.Zip64ExtraDataInfo Zip64ExtraData { get; set; } = null;

        /// <summary>Unicode Path ExtraData</summary>
        public ZipHeader.UnicodePathExtraDataField UnicodePathExtraData { get; set; } = null;

		/// <summary>DataDescriptor(PK0708)を出力する</summary>
		public bool WriteDataDescriptor { get; set; } = false;

		/// <summary>ディレクトリであることを表す</summary>
		public bool IsDirectory
        {
            get
            {
				return (FileAttribute & FileAttributes.Directory) == FileAttributes.Directory;
			}
        }

		public string FullName { get; set; }

	}
}
