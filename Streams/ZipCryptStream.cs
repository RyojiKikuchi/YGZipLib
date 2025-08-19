using System;
using System.IO;
using System.Security.Cryptography;
#if YGZIPLIB
using YGZipLib.Common;
using YGZipLib.Streams;
using YGZipLib.Properties;
#elif YGMAILLIB
using YGMailLib.Zip.Common;
using YGMailLib.Zip.Streams;
using YGMailLib.Zip.Properties;
#endif

#if YGZIPLIB
namespace YGZipLib.Streams
#elif YGMAILLIB
namespace YGMailLib.Zip.Streams
#endif
{
    /// <summary>
    /// ZIP暗号化クラス
    /// </summary>
    /// <remarks></remarks>
    internal class ZipCryptStream : Stream
	{

        #region enum
        public enum StreamMode
        {
            ENCRYPT,
            DECRYPT
        }

        #endregion

        #region const

        private const uint KEY0_INITIAL = 0x12345678u;
        private const uint KEY1_INITIAL = 0x23456789u;
        private const uint KEY2_INITIAL = 0x34567890u;
        private const ulong KEY_UPDATE_FACTOR = 134775813uL;

        #endregion

        #region struct

        private unsafe struct FixedBuffer
        {
            public fixed UInt32 crcTable[256];
            public fixed UInt32 key[3];
        }

        #endregion

        #region variables

        private FixedBuffer fixedBuf = default;
        private Stream writeStream = null;
        private readonly InputStream readStream = null;
        private readonly StreamMode streamMode;

        #endregion

        #region コンストラクタ

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseStream">ベースストリーム</param>
        /// <param name="password">パスワード</param>
        /// <param name="crc32">暗号化前のCRC</param>
        /// <param name="streamMode">暗号化or復号化</param>
        /// <remarks></remarks>
        public ZipCryptStream(Stream baseStream, byte[] password, uint crc32, StreamMode streamMode)
		{
			this.streamMode = streamMode;
            unsafe
            {
                fixed (UInt32* crcTable = fixedBuf.crcTable)
                {
                    ShareMethodClass.CopyCrcTable(crcTable);
                }
				//
                fixedBuf.key[0] = KEY0_INITIAL;
                fixedBuf.key[1] = KEY1_INITIAL;
                fixedBuf.key[2] = KEY2_INITIAL;
            }

            if (this.streamMode == StreamMode.ENCRYPT)
			{
				this.writeStream = baseStream;
				InitZipEncrypt(password, crc32);
			}
			else
			{
                if (!(baseStream is InputStream))
                {
                    throw new ArgumentException("baseStream must be InputStream for DECRYPT mode.");
                }
                this.readStream = (InputStream)baseStream;
				InitZipDecrypt(password, crc32);
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// 1Byte暗号化する
		/// </summary>
		/// <param name="n">暗号化前のByte値</param>
		/// <returns>暗号化後のByte値</returns>
		/// <remarks></remarks>
		private byte ZEncode(byte n)
		{
			unsafe
			{
                uint temp = (fixedBuf.key[2] & 0xFFFFu) | 2u;
                byte encoded = (byte)((byte)((temp * (temp ^ 1) >> 8) & 0xFFu) ^ n);
                UpdateKeys(n);
                return encoded;
            }
        }

		/// <summary>
		/// 1Byte復号化する
		/// </summary>
		/// <param name="n">復号化前のByte値</param>
		/// <returns>復号化後のByte値</returns>
		/// <remarks></remarks>
		private byte ZDecode(byte n)
		{
			unsafe
			{
                uint temp = (fixedBuf.key[2] & 0xFFFFu) | 2u;
                byte decoded = (byte)((byte)((temp * (temp ^ 1) >> 8) & 0xFFu) ^ n);
                UpdateKeys(decoded);
                return decoded;
            }
        }

		/// <summary>
		/// ZIP暗号化キー更新
		/// </summary>
		/// <param name="n"></param>
		/// <remarks></remarks>
		private void UpdateKeys(byte n)
		{

			unsafe
			{
				fixedBuf.key[0] = fixedBuf.crcTable[(int)((fixedBuf.key[0] ^ n) & 0xFFu)] ^ (fixedBuf.key[0] >> 8);
				fixedBuf.key[1] = (uint)((((((ulong)fixedBuf.key[0] & 0xFFuL) + fixedBuf.key[1]) & 0xFFFFFFFFu) * KEY_UPDATE_FACTOR + 1uL) & 0xFFFFFFFFu);
				fixedBuf.key[2] = fixedBuf.crcTable[(int)((fixedBuf.key[2] ^ (fixedBuf.key[1] >> 24)) & 0xFFu)] ^ (fixedBuf.key[2] >> 8);
			}

		}

		/// <summary>
		/// ZIP暗号化初期化(復号化)
		/// </summary>
		/// <param name="password"></param>
		/// <param name="crc32"></param>
		/// <remarks></remarks>
		private void InitZipDecrypt(byte[] password, uint crc32)
		{
			// パスワードでキーを更新
			for (int j = 0; j < password.Length; j++)
			{
				UpdateKeys(password[j]);
			}
			// 暗号化ヘッダ読込
			byte[] cryptHeader = new byte[12];
			this.readStream.ReadBuffer(cryptHeader);
			byte checkCrc = 0;
			for (int i = 0; i < cryptHeader.Length; i++)
			{
				checkCrc = ZDecode(cryptHeader[i]);
			}
			if (checkCrc != (byte)((crc32 >> 24) & 0xFFu))
			{
				throw new CryptographicException(Resources.ERRMSG_INCORRECT_PASSWORD);
			}
		}

		/// <summary>
		/// ZIP暗号化初期化(暗号化)
		/// </summary>
		/// <param name="password"></param>
		/// <param name="crc32"></param>
		/// <remarks></remarks>
		private void InitZipEncrypt(byte[] password, uint crc32)
		{
			// パスワードでキーを更新
			for (int j = 0; j < password.Length; j++)
			{
				UpdateKeys(password[j]);
			}

			// 暗号化ヘッダ
			byte[] cryptHeader = new byte[12];

			// 先頭11バイトに乱数(IV)設定
			using (RandomNumberGenerator randomGen = RandomNumberGenerator.Create())
			{
				randomGen.GetBytes(cryptHeader);
			}

			// 最後の1バイトはCRCから設定
			cryptHeader[11] = (byte)((crc32 >> 24) & 0xFFu);

			for (int i = 0; i < cryptHeader.Length; i++)
			{
				cryptHeader[i] = ZEncode(cryptHeader[i]);
			}

			writeStream.Write(cryptHeader, 0, cryptHeader.Length);
		}

		#endregion

		#region override

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
			if (streamMode == StreamMode.DECRYPT)
			{
				throw new NotSupportedException();
			}
			writeStream.Flush();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			if (streamMode == StreamMode.DECRYPT)
			{
				throw new NotSupportedException();
			}

			uint temp;

			// 1バイト毎に暗号化していく
			unsafe {
				fixed(byte* b = buffer)
				{
					fixed(uint* k = fixedBuf.key)
					{
                        byte* bp = &b[offset];
                        uint* k0 = &k[0]; uint* k1 = &k[1]; uint* k2 = &k[2];
                        for (int i = 0; i < count; i++)
                        {

							// 暗号化(処理速度改善のためZEncodeとUpdateKeysを展開)
							temp = (*k2 & 0xFFFFu) | 2u;
							*k0 = fixedBuf.crcTable[(*k0 ^ *bp) & 0xFF] ^ (*k0 >> 8);
							*k1 = (uint)((((((ulong)*k0 & 0xFFuL) + *k1) & 0xFFFFFFFFu) * KEY_UPDATE_FACTOR + 1) & 0xFFFFFFFFu);
							*k2 = fixedBuf.crcTable[(*k2 ^ (*k1 >> 24)) & 0xFF] ^ (*k2 >> 8);
							*bp = (byte)((byte)((temp * (temp ^ 1) >> 8) & 0xFFu) ^ *bp);
							bp++;

						}
                    }
                }
            }
			writeStream.Write(buffer, offset, count);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (streamMode == StreamMode.ENCRYPT)
			{
				throw new NotSupportedException();
			}

			uint temp;

            // １バイトごとに復号化していく
            int readCount = readStream.Read(buffer, offset, count);
			unsafe
			{
				fixed (byte* b = buffer)
				{
					fixed(uint* k= fixedBuf.key)
					{
                        byte* bp = &b[offset];
                        uint* k0 = &k[0]; uint* k1 = &k[1]; uint* k2 = &k[2];
                        for (int i = 0; i < readCount; i++)
                        {

							// 復号化(処理速度改善のためZDecodeとUpdateKeysを展開)
							temp = (*k2 & 0xFFFFu) | 2u;
							*bp = (byte)((byte)((temp * (temp ^ 1) >> 8) & 0xFFu) ^ *bp);
							*k0 = fixedBuf.crcTable[(*k0 ^ *bp) & 0xFF] ^ (*k0 >> 8);
							*k1 = (uint)((((((ulong)*k0 & 0xFFuL) + *k1) & 0xFFFFFFFFu) * KEY_UPDATE_FACTOR + 1) & 0xFFFFFFFFu);
							*k2 = fixedBuf.crcTable[(*k2 ^ (*k1 >> 24)) & 0xFF] ^ (*k2 >> 8);
							bp++;

						}
                    }
                }
            }
            return readCount;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (writeStream != null)
				{
					writeStream = null;
				}
				readStream?.Dispose();
			}
			base.Dispose(disposing);
		}

		#endregion

	}
}
