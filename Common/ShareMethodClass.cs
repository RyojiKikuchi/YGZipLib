using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using static YGZipLib.UnZipArcClass;
using System.Runtime.CompilerServices;




#if YGZIPLIB
using YGZipLib.Common;
using YGZipLib.Streams;
#elif YGMAILLIB
using YGMailLib.Zip.Common;
using YGMailLib.Zip.Streams;
#endif

#if YGZIPLIB
namespace YGZipLib.Common
#elif YGMAILLIB
namespace YGMailLib.Zip.Common
#endif
{

    /// <summary>共有メソッドクラス</summary>
    internal static class ShareMethodClass
    {

        #region Const

        //public const int BUFFER_COUNT = 16384;

        #endregion

        #region Member/Variables

        /// <summary>システム規定のANSIコードページ</summary>
        private static readonly int defaultAnsiCodepage;
        /// <summary>システム規定の置換文字列</summary>
        private static readonly string defaultReplacementString;

        /// <summary>ファイル名として使用不可能な文字</summary>
        private static readonly HashSet<char> invFileNameChars = new HashSet<char>();
        /// <summary>ディレクトリ名として使用不可能な文字</summary>
        private static readonly HashSet<char> invPathNameChars = new HashSet<char>();

        private static readonly object encLockObj = new object();

        private static readonly Encoding utf8Encoding = Encoding.UTF8;

        private static readonly Encoding asciiEncoding = Encoding.GetEncoding(Encoding.ASCII.CodePage, new EncoderReplacementFallback("-"), new DecoderReplacementFallback("-"));

        private static readonly Encoding ansiEncoding;

        /// <summary>CRC32Table</summary>
        private static readonly UInt32[] crcTable = {
            0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA, 0x076DC419, 0x706AF48F, 0xE963A535, 0x9E6495A3,
            0x0EDB8832, 0x79DCB8A4, 0xE0D5E91E, 0x97D2D988, 0x09B64C2B, 0x7EB17CBD, 0xE7B82D07, 0x90BF1D91,
            0x1DB71064, 0x6AB020F2, 0xF3B97148, 0x84BE41DE, 0x1ADAD47D, 0x6DDDE4EB, 0xF4D4B551, 0x83D385C7,
            0x136C9856, 0x646BA8C0, 0xFD62F97A, 0x8A65C9EC, 0x14015C4F, 0x63066CD9, 0xFA0F3D63, 0x8D080DF5,
            0x3B6E20C8, 0x4C69105E, 0xD56041E4, 0xA2677172, 0x3C03E4D1, 0x4B04D447, 0xD20D85FD, 0xA50AB56B,
            0x35B5A8FA, 0x42B2986C, 0xDBBBC9D6, 0xACBCF940, 0x32D86CE3, 0x45DF5C75, 0xDCD60DCF, 0xABD13D59,
            0x26D930AC, 0x51DE003A, 0xC8D75180, 0xBFD06116, 0x21B4F4B5, 0x56B3C423, 0xCFBA9599, 0xB8BDA50F,
            0x2802B89E, 0x5F058808, 0xC60CD9B2, 0xB10BE924, 0x2F6F7C87, 0x58684C11, 0xC1611DAB, 0xB6662D3D,
            0x76DC4190, 0x01DB7106, 0x98D220BC, 0xEFD5102A, 0x71B18589, 0x06B6B51F, 0x9FBFE4A5, 0xE8B8D433,
            0x7807C9A2, 0x0F00F934, 0x9609A88E, 0xE10E9818, 0x7F6A0DBB, 0x086D3D2D, 0x91646C97, 0xE6635C01,
            0x6B6B51F4, 0x1C6C6162, 0x856530D8, 0xF262004E, 0x6C0695ED, 0x1B01A57B, 0x8208F4C1, 0xF50FC457,
            0x65B0D9C6, 0x12B7E950, 0x8BBEB8EA, 0xFCB9887C, 0x62DD1DDF, 0x15DA2D49, 0x8CD37CF3, 0xFBD44C65,
            0x4DB26158, 0x3AB551CE, 0xA3BC0074, 0xD4BB30E2, 0x4ADFA541, 0x3DD895D7, 0xA4D1C46D, 0xD3D6F4FB,
            0x4369E96A, 0x346ED9FC, 0xAD678846, 0xDA60B8D0, 0x44042D73, 0x33031DE5, 0xAA0A4C5F, 0xDD0D7CC9,
            0x5005713C, 0x270241AA, 0xBE0B1010, 0xC90C2086, 0x5768B525, 0x206F85B3, 0xB966D409, 0xCE61E49F,
            0x5EDEF90E, 0x29D9C998, 0xB0D09822, 0xC7D7A8B4, 0x59B33D17, 0x2EB40D81, 0xB7BD5C3B, 0xC0BA6CAD,
            0xEDB88320, 0x9ABFB3B6, 0x03B6E20C, 0x74B1D29A, 0xEAD54739, 0x9DD277AF, 0x04DB2615, 0x73DC1683,
            0xE3630B12, 0x94643B84, 0x0D6D6A3E, 0x7A6A5AA8, 0xE40ECF0B, 0x9309FF9D, 0x0A00AE27, 0x7D079EB1,
            0xF00F9344, 0x8708A3D2, 0x1E01F268, 0x6906C2FE, 0xF762575D, 0x806567CB, 0x196C3671, 0x6E6B06E7,
            0xFED41B76, 0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC, 0xF9B9DF6F, 0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5,
            0xD6D6A3E8, 0xA1D1937E, 0x38D8C2C4, 0x4FDFF252, 0xD1BB67F1, 0xA6BC5767, 0x3FB506DD, 0x48B2364B,
            0xD80D2BDA, 0xAF0A1B4C, 0x36034AF6, 0x41047A60, 0xDF60EFC3, 0xA867DF55, 0x316E8EEF, 0x4669BE79,
            0xCB61B38C, 0xBC66831A, 0x256FD2A0, 0x5268E236, 0xCC0C7795, 0xBB0B4703, 0x220216B9, 0x5505262F,
            0xC5BA3BBE, 0xB2BD0B28, 0x2BB45A92, 0x5CB36A04, 0xC2D7FFA7, 0xB5D0CF31, 0x2CD99E8B, 0x5BDEAE1D,
            0x9B64C2B0, 0xEC63F226, 0x756AA39C, 0x026D930A, 0x9C0906A9, 0xEB0E363F, 0x72076785, 0x05005713,
            0x95BF4A82, 0xE2B87A14, 0x7BB12BAE, 0x0CB61B38, 0x92D28E9B, 0xE5D5BE0D, 0x7CDCEFB7, 0x0BDBDF21,
            0x86D3D2D4, 0xF1D4E242, 0x68DDB3F8, 0x1FDA836E, 0x81BE16CD, 0xF6B9265B, 0x6FB077E1, 0x18B74777,
            0x88085AE6, 0xFF0F6A70, 0x66063BCA, 0x11010B5C, 0x8F659EFF, 0xF862AE69, 0x616BFFD3, 0x166CCF45,
            0xA00AE278, 0xD70DD2EE, 0x4E048354, 0x3903B3C2, 0xA7672661, 0xD06016F7, 0x4969474D, 0x3E6E77DB,
            0xAED16A4A, 0xD9D65ADC, 0x40DF0B66, 0x37D83BF0, 0xA9BCAE53, 0xDEBB9EC5, 0x47B2CF7F, 0x30B5FFE9,
            0xBDBDF21C, 0xCABAC28A, 0x53B39330, 0x24B4A3A6, 0xBAD03605, 0xCDD70693, 0x54DE5729, 0x23D967BF,
            0xB3667A2E, 0xC4614AB8, 0x5D681B02, 0x2A6F2B94, 0xB40BBE37, 0xC30C8EA1, 0x5A05DF1B, 0x2D02EF8D};

        #endregion

        #region コンストラクタ

        /// <summary>静的コンストラクタ</summary>
        static ShareMethodClass()
		{
            // 使用不可文字の設定
            Path.GetInvalidPathChars().ToList().ForEach(c => {
                invPathNameChars.Add(c);
            });
			if (invPathNameChars.Contains('*') == false) invPathNameChars.Add('*');
            if (invPathNameChars.Contains('?') == false) invPathNameChars.Add('?');
            Path.GetInvalidFileNameChars().ToList().ForEach(c => {
                invFileNameChars.Add(c);
            });
            if (invFileNameChars.Contains('*') == false) invFileNameChars.Add('*');
            if (invFileNameChars.Contains('?') == false) invFileNameChars.Add('?');

            // コードページ取得
            try
            {
                defaultAnsiCodepage = System.Globalization.CultureInfo.CurrentUICulture.TextInfo.ANSICodePage;
            }
            catch
            {
                defaultAnsiCodepage = 65001;
            }
            Encoding enc;
            try
            {
                enc = Encoding.GetEncoding(defaultAnsiCodepage);
            }
#if DEBUG
			catch (NotSupportedException ex)
            {
                Debug.WriteLine($"ShareMethodClass static constructor : GetEncoding throw exception. ex={ex}");
#else
            catch (NotSupportedException)
			{ 
#endif
                defaultAnsiCodepage = 65001;
                enc = Encoding.GetEncoding(defaultAnsiCodepage);
            }
            catch (Exception)
            {
                throw;
            }

            // ReplacementString取得
            try
            {
                if (string.Compare(enc.GetString(enc.GetBytes("〓")), "〓", StringComparison.Ordinal) == 0)
                {
                    defaultReplacementString = "〓";
                }
                else
                {
                    defaultReplacementString = "-";
                }
            }
            catch { defaultReplacementString = "-"; }

#if DEBUG
            Debug.WriteLine($"ShareMethodClass static constructor : Encoding={enc.EncodingName}, Codepage={defaultAnsiCodepage}, ReplacementString={defaultReplacementString}");
#endif
			ansiEncoding = Encoding.GetEncoding(defaultAnsiCodepage, new EncoderReplacementFallback(defaultReplacementString), new DecoderReplacementFallback(defaultReplacementString));
        }

        #endregion

        #region Stream I/O

        /// <summary>
        /// ストリームから指定されたバッファ長分読み込む。途中でストリームの終わりになった場合、例外が発生する。
        /// </summary>
        /// <param name="st"></param>
        /// <param name="buffer"></param>
        /// <remarks></remarks>
        internal static void StreamReadBuffer(Stream st, byte[] buffer)
		{
			int readPointer = 0;
			do
			{
				int readCount = st.Read(buffer, readPointer, buffer.Length - readPointer);
				if (readCount == 0)
				{
					throw new EndOfStreamException();
				}
				readPointer += readCount;
			}
			while (readPointer < buffer.Length);
		}

        #endregion

        #region DOS file time stamp

        /// <summary>
        /// DOS形式の日付編集
        /// </summary>
        /// <param name="fileTime"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        internal static UInt16 EncodeDosFileDate(DateTime fileTime)
		{
			// bit 0-4 day
			// bit 5-8 month
			// bit 9-15 year
			UInt16 y = (UInt16)(fileTime.Year - 1980);
			UInt16 m = (UInt16)fileTime.Month;
			UInt16 d = (UInt16)fileTime.Day;
			return (UInt16)((UInt16)(y << 9) | (UInt16)(m << 5) | d);
		}

		/// <summary>
		/// DOS形式の日付編集
		/// </summary>
		/// <param name="fileTimeStamp"></param>
		/// <returns></returns>
		/// <remarks></remarks>
		internal static UInt16 EncodeDosFileTime(DateTime fileTimeStamp)
		{
			// bit 0-4 sec
			// bit 5-10 minute
			// bit 11-15 hour
			UInt16 h = (UInt16)fileTimeStamp.Hour;
			UInt16 m = (UInt16)fileTimeStamp.Minute;
			UInt16 s = (UInt16)(fileTimeStamp.Second / 2);
			return (UInt16)((UInt16)(h << 11) | (UInt16)(m << 5) | s);
		}

		/// <summary>
		/// DOS形式の日付をDateTime型に変換
		/// </summary>
		/// <param name="fileDate"></param>
		/// <param name="fileTime"></param>
		/// <returns></returns>
		/// <remarks></remarks>
		internal static DateTime DecodeDosFileDateTime(ushort fileDate, ushort fileTime)
		{
			int yyyy = ((ushort)((uint)fileDate >> 9) & 0x7F) + 1980;
			int mm = (ushort)((uint)fileDate >> 5) & 0xF;
			int dd = fileDate & 0x1F;
			int hh24 = (ushort)((uint)fileTime >> 11) & 0x1F;
			int mi = (ushort)((uint)fileTime >> 5) & 0x3F;
			int ss = (fileTime & 0x1F) * 2;
			try
			{
				return new DateTime(yyyy, mm, dd, hh24, mi, ss);
			}
			catch 
			{
				return DateTime.MinValue;
			}
		}

        #endregion

        #region CRC Table

        /// <summary>
        /// CRCテーブルコピー
        /// </summary>
        /// <param name="destCrc"></param>
        internal static unsafe void CopyCrcTable(uint* destCrc)
		{
			fixed(uint* sourceCrc = crcTable)
			{
				ulong* s = (ulong*)sourceCrc;
				ulong* d = (ulong*)destCrc;
				for(int i = 0; i < 128; i++)
				{
					*(d++) = *(s++);
				}
			}
		}

        #endregion

        #region Byte Array

        /// <summary>
        /// バイト配列が等しいかチェックする
        /// </summary>
        /// <param name="byteArray1"></param>
        /// <param name="byteArray2"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        internal static unsafe bool ByteArrayCompare(byte[] byteArray1, byte[] byteArray2)
		{
            if (byteArray1.Length != byteArray2.Length)
            {
                return false;
            }

            int len = byteArray1.Length;
            fixed (byte* bypeArrayPointer1 = byteArray1, byteArrayPointer2 = byteArray2)
            {
                long* longConvPointer1 = (long*)bypeArrayPointer1;
                long* longConvPointer2 = (long*)byteArrayPointer2;
                for (; len >= 8; len -= 8)
                {
                    if (*(longConvPointer1++) != *(longConvPointer2++))
                    {
                        return false;
                    }
                }
                byte* byteConvPointer1 = (byte*)longConvPointer1;
                byte* byteConvPointer2 = (byte*)longConvPointer2;
                for (; len > 0; len--)
                {
                    if (*(byteConvPointer1++) != *(byteConvPointer2++))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        #endregion

        #region Encodings

        /// <summary>
        /// システム規定のANSIエンコーディング
        /// </summary>
        /// <returns></returns>
        /// <remarks></remarks>
        public static Encoding AnsiEncoding
        {
            get
            {
                return ansiEncoding;
            }
        }

        /// <summary>
        /// UTF-8のエンコーディング(codepage=65001)
        /// </summary>
        public static Encoding Utf8Encoding
        {
            get
            {
                return utf8Encoding;
            }
        }

        /// <summary>
        /// US-ASCII(codepage=20127)
        /// </summary>
		public static Encoding AsciiEncoding
		{
			get
			{
				return asciiEncoding;
			}
		}

        /// <summary>StringをByte配列に変換する(スレッドセーフ)</summary>
		internal static byte[] EncodingGetBytes(string text, Encoding enc)
		{
			byte[] ret = null;
			EncodingGet(enc,ref text, ref ret);
			return ret;
		}

        /// <summary>Byte配列をStringに変換する(スレッドセーフ)</summary>
		internal static string EncodingGetString(byte[] byteArray, Encoding enc)
		{
            string ret = null;
            EncodingGet(enc, ref ret, ref byteArray);
			return ret;
		}

        /// <summary>
        /// EncodingのGetString,GetBytesをスレッドセーフにするためのメソッド
        /// </summary>
        /// <param name="enc"></param>
        /// <param name="text"></param>
        /// <param name="byteArray"></param>
        /// <remarks><see cref="EncodingGetBytes(string, Encoding)"/>または<see cref="EncodingGetString(byte[], Encoding)"/>から呼び出される。</remarks>
		private static void EncodingGet(Encoding enc,ref string text,ref byte[] byteArray)
		{
            // EncodingのGetStrings,GetBytesがスレッドセーフではなさそうなのでlockしておく
            lock (encLockObj)
			{
                if (text == null)
                {
                    text = enc.GetString(byteArray);
                }
                else
                {
                    byteArray = enc.GetBytes(text);
                }
            }
        }

        #endregion

        #region 変換系

        internal static UInt16 IntToUInt(Int16 i)
		{
			return BitConverter.ToUInt16(BitConverter.GetBytes(i), 0);
		}

        internal static UInt32 IntToUInt(Int32 i)
		{
			return BitConverter.ToUInt32(BitConverter.GetBytes(i), 0);
		}

        internal static UInt64 IntToUInt(Int64 i)
		{
			return BitConverter.ToUInt64(BitConverter.GetBytes(i), 0);
		}

        internal static Int16 UIntToInt(UInt16 ui)
		{
			return BitConverter.ToInt16(BitConverter.GetBytes(ui), 0);
		}

        internal static Int32 UIntToInt(UInt32 ui)
		{
			return BitConverter.ToInt32(BitConverter.GetBytes(ui), 0);
		}

        internal static Int64 UIntToInt(UInt64 ui)
		{
			return BitConverter.ToInt64(BitConverter.GetBytes(ui), 0);
		}

        internal static string CompTypeString(ushort comptype, ushort generalPurposeFlg)
		{
			switch ((int)comptype)
			{
				case 0x00:
					return "stored";
				case 0x01:
					return "*Shrunk";
				case 0x08:
                    return $"deflate{CompOptionString(comptype, generalPurposeFlg)}";
				case 0x09:
#if DEFLATE64
					return $"deflate64{CompOptionString(comptype, generalPurposeFlg)}";
#else
                    return $"*deflate64{CompOptionString(comptype, generalPurposeFlg)}";
#endif
                case 0x0c:
					return "*BZip2";
				case 0x0e:
					return $"*LZMA{CompOptionString(comptype, generalPurposeFlg)}";
                case 0x5d:
                    return "*zstd";
                case 0x5f:
                    return "*xz";
                case 0x62:
					return "*PPMd";
				case 0x63:
					return "AES Enc";
			}
			return "*unknown";
		}

        private static string CompOptionString(ushort comptype, ushort generalPurposeFlg)
        {
            switch ((int)comptype)
            {
                case 0x08:
                case 0x09:
                    switch ((generalPurposeFlg & 0b0000000000000110) >> 1)
                    {
                        case 0b00:
                            return string.Empty;
                        case 0b01:
                            return "(maximum)";
                        case 0b10:
                            return "(fast)";
                        case 0b11:
                            return "(superfast)";
                    }
                    break;
                case 0x0e:
                    switch ((generalPurposeFlg & 0b0000000000000010) >> 1)
                    {
                        case 0b01:
                            return "(eos)";
                    }
                    break;
            }
            return string.Empty;
        }

        internal static string ExternalFileAttributesString(UnZipArcClass.MadeOs os, uint extAttr)
        {
            StringBuilder sb = new StringBuilder();
            switch (os)
            {
                case MadeOs.MSDOS:
                case MadeOs.NTFS:
                case MadeOs.VFAT:
                case MadeOs.WindowsNT:
                    if ((extAttr & (UInt32)FileAttributes.ReadOnly) > 0)
                        sb.Append("|ReadOnly");
                    if ((extAttr & (UInt32)FileAttributes.Hidden) > 0)
                        sb.Append("|Hidden");
                    if ((extAttr & (UInt32)FileAttributes.System) > 0)
                        sb.Append("|System");
                    if ((extAttr & (UInt32)FileAttributes.Directory) > 0)
                        sb.Append("|Directory");
                    if ((extAttr & (UInt32)FileAttributes.Archive) > 0)
                        sb.Append("|Archive");
                    if ((extAttr & (UInt32)FileAttributes.Normal) > 0)
                        sb.Append("|Normal");

                    break;
                case UnZipArcClass.MadeOs.UNIX:
                    if ((extAttr & UnZipArcClass.UNIX_S_IFREG) > 0)
                    {
                        sb.Append("|S_IFREG");
                    }
                    if ((extAttr & UnZipArcClass.UNIX_S_IFDIR) > 0)
                    {
                        sb.Append("|S_IFDIR");
                    }
                    if ((extAttr & UnZipArcClass.UNIX_S_IFLNK) > 0)
                    {
                        sb.Append("|S_IFLNK");
                    }
                    break;
                default:
                    break;
            }
            if (sb.Length > 0)
            {
                return sb.ToString().Substring(1);
            }
            return sb.ToString();
        }

        internal static string GeneralPurposeBitString(ushort generalPurposeFlg)
		{
			StringBuilder sb = new StringBuilder();
            if ((generalPurposeFlg & 0b0000000000000001) > 0)
                sb.Append("|encrypted");
            if ((generalPurposeFlg & 0b0000000000001000) > 0)
                sb.Append("|data descriptor");
            if ((generalPurposeFlg & 0b0000000000010000) > 0)
                sb.Append("|enhanced deflating");
            if ((generalPurposeFlg & 0b0000000000100000) > 0)
                sb.Append("|patched data");
            if ((generalPurposeFlg & 0b0000000001000000) > 0)
                sb.Append("|strong encryption");
            if ((generalPurposeFlg & 0b0000100000000000) > 0)
                sb.Append("|utf-8");
            if ((generalPurposeFlg & 0b0001000000000000) > 0)
                sb.Append("|enhanced compression");
            if ((generalPurposeFlg & 0b0010000000000000) > 0)
                sb.Append("|central directory encrypted");
            if (sb.Length > 0)
			{
				return sb.ToString().Substring(1);
			}
			return sb.ToString();
		}

        internal static string MadeVerString(ushort madeVer)
        {
            StringBuilder sb= new StringBuilder();
            switch((UnZipArcClass.MadeOs)(madeVer >> 8))
            {
                case MadeOs.MSDOS:
                    sb.Append("MS-DOS");
                    break;
                case MadeOs.UNIX:
                    sb.Append("UNIX");
                    break;
                case MadeOs.OS2:
                    sb.Append("OS/2 HPFS");
                    break;
                case MadeOs.Macintosh:
                    sb.Append("Macintosh");
                    break;
                case MadeOs.Z_SYSTEM:
                    sb.Append("Z-System");
                    break;
                case MadeOs.NTFS:
                    sb.Append("Windows NTFS");
                    break;
                case MadeOs.VFAT:
                    sb.Append("VFAT");
                    break;
                case MadeOs.WindowsNT:
                    sb.Append("WindownNT");
                    break;
                case MadeOs.OSX:
                    sb.Append("OS X");
                    break;
                default:
                    sb.Append("Unknown OS");
                    break;
            }
            sb.Append($" {VersionString(madeVer)}");
            return sb.ToString();
        }

        internal static string VersionString(ushort madeVer)
        {
            int version = madeVer & 0x00FF;
            if (version == 0)
            {
                return "1.0";
            }
            return $"{version / 10}.{version % 10}";
        }


        /// <summary>
        /// byte配列を文字列に変換
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        internal static string ByteArrayToHex(byte[] bytes)
        {
            return ByteArrayToHex(bytes, false);
        }

        /// <summary>
        /// byte配列を文字列に変換
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="separator">区切り文字を入れるか</param>
        /// <returns></returns>
        internal static string ByteArrayToHex(byte[] bytes, bool separator)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 3);
            foreach (byte b in bytes)
            {
                if (separator && sb.Length > 0)
                {
                    sb.Append(':');
                }
                sb.Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        #endregion

        #region ファイル／ディレクトリ名チェック

        /// <summary>
        /// ディレクトリ名に使用不可文字が含まれていないかチェックする
        /// </summary>
        /// <param name="pathName"></param>
        /// <returns></returns>
        internal static unsafe Boolean CheckPathName(string pathName)
		{
            if (string.IsNullOrEmpty(pathName)) return true;
            fixed (char* pp = pathName)
            {
                char* pp2 = pp;
                for (int i = 0; i < pathName.Length; i++)
                {
                    if (invPathNameChars.Contains(*pp2++))
                    {
                        return false;
                    }
                }
            }
            return true;
		}

        /// <summary>
        /// ファイル名に使用不可文字が含まれていないかチェックする
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        internal static unsafe bool CheckFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            fixed (char* fp = fileName)
            {
                char* fpp = fp;
                for (int i = 0; i < fileName.Length; i++)
                {
                    if (invFileNameChars.Contains(*fpp++))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        internal static string ReplaceInvPathChar(string pathName)
		{
            if (string.IsNullOrEmpty(pathName)) return pathName;
            StringBuilder sb = new StringBuilder();
            foreach (char c in pathName)
            {
                if (invPathNameChars.Contains(c))
                {
                    sb.Append('-');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        internal static string ReplaceInvFileChar(string fileName)
		{
            if (string.IsNullOrEmpty(fileName)) return fileName;
            StringBuilder sb = new StringBuilder();
            foreach (char c in fileName)
            {
                if (invFileNameChars.Contains(c))
                {
                    sb.Append('-');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        #endregion

        #region ZIPファイルチェック

#if DEBUG && YGZIPLIB

        /// <summary>
        /// セントラルディレクトリのチェック
        /// </summary>
        /// <param name="unzip"></param>
        /// <param name="cds"></param>
        /// <returns></returns>
        internal static string CheckDirectories(UnZipArcClass unzip, List<UnZipArcClass.CentralDirectoryInfo> cds)
		{
            StringBuilder sb = new StringBuilder();
            //using (SemaphoreSlim sem = new SemaphoreSlim(8))
            //{
                Parallel.ForEach(cds, cd => {

                    StringBuilder res = new StringBuilder();
                    try
                    {
                        //sem.Wait();
                        string cdErrors = CheckDirectory(unzip, cd);
                        if (string.IsNullOrWhiteSpace(cdErrors))
                        {
                            res.Append($"OK {cd.FullName}");
                        }
                        else
                        {
                            res.Append($"Error {cd.FullName} {cdErrors}, \r\n{cd}, \r\n{cd.LocalHeaderString}");
                        }
                    }
                    catch (Exception ex)
                    {
                        res.Append($"Error {cd.FullName} {ex}, \r\n{cd}, \r\n{cd.LocalHeaderString}");
                    }
                    finally
                    {
                        //sem.Release();
                    }
                    lock (sb)
                    {
                        sb.AppendLine($"{res}");
                    }
                });
            //}

            return sb.ToString();
        }

        /// <summary>
        /// セントラルディレクトリの1エントリチェック
        /// </summary>
        /// <param name="unzip"></param>
        /// <param name="cd"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        internal static string CheckDirectory(UnZipArcClass unzip, UnZipArcClass.CentralDirectoryInfo cd)
		{
            List<string> errors = new List<string>();

			// LocalHeader取得
			ZipHeader.PK0304Info pk0304 = cd.LocalHeader;

            // CentralDirectoryとLocalHeaderのチェック
            string localHeaderErrors = CompareCdToLc(cd, pk0304);
            if(string.IsNullOrWhiteSpace(localHeaderErrors) == false)
            {
                errors.Add($"Incorrect LocalHeader({localHeaderErrors})");
            }

            if (cd.IsDirectory == false)
            {
                using (Stream st = Stream.Null)
                using (CalcCrc32Stream crc32 = new CalcCrc32Stream(st, CalcCrc32Stream.StreamMode.WRITE))
                {
                    unzip.PutFile(cd, crc32);
                    if (cd.ExtraFields.ContainsKey(ZipHeader.ExtraDataId.AesExtraData))
                    {
                        if (cd.GetAesExtraData().VersionNumber == 1)
                        {
                            if (crc32.Crc32 != cd.Pk0102.Crc32)
                            {
                                errors.Add($"Error Crc32(Unzipfile:{crc32.Crc32}\r\nPK0102:{cd.Pk0102.Crc32})");
                            }
                        }
                    }
                    else
                    {
                        if (crc32.Crc32 != cd.Pk0102.Crc32)
                        {
                            errors.Add($"Error Crc32(Unzipfile:{crc32.Crc32}\r\nPK0102:{cd.Pk0102.Crc32})");
                        }
                    }
                }
            }

            return string.Join("/", errors);

        }

        internal static string CompareCdToLc(UnZipArcClass.CentralDirectoryInfo cd, ZipHeader.PK0304Info lc)
        {
            List<string> errors = new List<string>();

            // Needver
            if (cd.Pk0102.Needver != lc.Needver)
            {
                errors.Add($"needver");
            }
            // Opt
            if (cd.Pk0102.Opt != lc.Opt)
            {
                errors.Add($"flag");
            }
            // Comptype
            if (cd.Pk0102.Comptype != lc.Comptype)
            {
                errors.Add($"comp method");
            }
            // TimeStamp
            if (cd.Pk0102.Filedate != lc.Filedate || cd.Pk0102.Filetime != lc.Filetime)
            {
                errors.Add($"timestamp");
            }
            if ((cd.Pk0102.Opt & 0b0000000000001000) == 0)
            {
                // Crc32
                if (cd.Pk0102.Crc32 != lc.Crc32)
                {
                    errors.Add($"crd32");
                }
                // Compsize
                if (cd.Pk0102.Compsize != lc.Compsize)
                {
                    errors.Add($"compsize");
                }
                // Uncompsize
                if (cd.Pk0102.Uncompsize != lc.Uncompsize)
                {
                    errors.Add($"uncompsize");
                }
            }
            // filenameb
            if (ByteArrayCompare(cd.Pk0102.filenameb, lc.filenameb) == false)
            {
                errors.Add($"filename");
            }

            // extraData
            foreach (var ext in cd.ExtraFields)
            {
                if (cd.LocalExtraFields.TryGetValue(ext.Key, out byte[] value))
                {
                    if (ByteArrayCompare(ext.Value, value) == false)
                    {
                        // 内容が相違でもエラーとしない判定
                        switch (ext.Key)
                        {
                            case ZipHeader.ExtraDataId.NtfsExtraData:
                                
                                break;
                            default:
                                errors.Add($"extra data({(ushort)ext.Key:X4})");
                                break;
                        }
                    }
                }
                else
                {
                    // ローカルヘッダに存在しなくてもエラーとしない判定
                    switch (ext.Key)
                    {
                        case ZipHeader.ExtraDataId.NtfsExtraData:
                            break;
                        default:
                            errors.Add($"extra data({(ushort)ext.Key:X4})");
                            break;
                    }
                }
            }

            return string.Join("/", errors);

        }


#endif

        #endregion

        #region 一時ファイル

        private const string TEMP_FILE_PREFIX = "zltmp";
        private static long tempFileCounter = 0;

        /// <summary>テンポラリファイル名取得</summary>
        internal static string GetTempFileName(string tempPath)
        {
            tempPath = Path.GetFullPath(tempPath ?? Path.GetTempPath());
            if (Directory.Exists(tempPath) == false)
            {
                throw new DirectoryNotFoundException($"Temporary directory does not exist. {tempPath}");
            }

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] randomBytes = new byte[8];
                rng.GetBytes(randomBytes);

                string tempFileName = Path.Combine(tempPath, $"{TEMP_FILE_PREFIX}_{Guid.NewGuid():N}_{ByteArrayToHex(randomBytes)}_{ByteArrayToHex(BitConverter.GetBytes(Interlocked.Increment(ref tempFileCounter)))}.tmp");
#if DEBUG
                Debug.WriteLine($"ShareMethodClass.GetTempFile : tempFileName={tempFileName}");
#endif
                return Path.GetFullPath(tempFileName);
            }

        }

#endregion

    }
}
