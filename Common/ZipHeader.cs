using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if YGZIPLIB
using YGZipLib.Streams;
#elif YGMAILLIB
using YGMailLib.Zip.Streams;
#endif

#if YGZIPLIB
namespace YGZipLib.Common
#elif YGMAILLIB
namespace YGMailLib.Zip.Common
#endif
{
    internal class ZipHeader
	{

        #region enum

		internal enum ExtraDataId : UInt16
		{
            Zip64ExtraData = (ushort)0x0001u,
            NtfsExtraData = (ushort)0x000au,
            ExtendedTimestamp = (ushort)0x5455u,
			UnicodePath = (ushort)0x7075,
            AesExtraData = (ushort)0x9901u
        }

        #endregion

        #region "PK0102 CentralDirectory"

        /// <summary>
        /// CentralDirectory(PK0102)クラス
        /// </summary>
        /// <remarks></remarks>
        [Serializable]
		internal class PK0102Info
		{
			private readonly byte[] signatureb = new byte[4];
			private readonly byte[] madeverb = new byte[2];
			private readonly byte[] needverb = new byte[2];
			private readonly byte[] optb = new byte[2];
			private readonly byte[] comptypeb = new byte[2];
			private readonly byte[] filetimeb = new byte[2];
			private readonly byte[] filedateb = new byte[2];
			private readonly byte[] crc32b = new byte[4];
			private readonly byte[] compsizeb = new byte[4];
			private readonly byte[] uncompsizeb = new byte[4];
			private readonly byte[] fnamelenb = new byte[2];
			private readonly byte[] extralenb = new byte[2];
			private readonly byte[] commentlenb = new byte[2];
			private readonly byte[] disknumb = new byte[2];
			private readonly byte[] inattrb = new byte[2];
			private readonly byte[] outattrb = new byte[4];
			private readonly byte[] headerposb = new byte[4];
			public byte[] filenameb = null;
			public byte[] extradatab = null;
			public byte[] commentb = null;
			public UInt32 Signature
			{
				get
				{
					return BytesToUInt32(signatureb);
				}
				set
				{
					UInt32ToBytes(value, signatureb);
				}
			}

			public UInt16 Madever
			{
				get
				{
					return BytesToUInt16(madeverb);
				}
				set
				{
					UInt16ToBytes(value, madeverb);
				}
			}

			public UInt16 Needver
			{
				get
				{
					return BytesToUInt16(needverb);
				}
				set
				{
					UInt16ToBytes(value, needverb);
				}
			}

			public UInt16 Opt
			{
				get
				{
					return BytesToUInt16(optb);
				}
				set
				{
					UInt16ToBytes(value, optb);
				}
			}

			public UInt16 Comptype
			{
				get
				{
					return BytesToUInt16(comptypeb);
				}
				set
				{
					UInt16ToBytes(value, comptypeb);
				}
			}

			public UInt16 Filetime
			{
				get
				{
					return BytesToUInt16(filetimeb);
				}
				set
				{
					UInt16ToBytes(value, filetimeb);
				}
			}

			public UInt16 Filedate
			{
				get
				{
					return BytesToUInt16(filedateb);
				}
				set
				{
					UInt16ToBytes(value, filedateb);
				}
			}

			public UInt32 Crc32
			{
				get
				{
					return BytesToUInt32(crc32b);
				}
				set
				{
					UInt32ToBytes(value, crc32b);
				}
			}

			public UInt32 Compsize
			{
				get
				{
					return BytesToUInt32(compsizeb);
				}
				set
				{
					UInt32ToBytes(value, compsizeb);
				}
			}

			public UInt32 Uncompsize
			{
				get
				{
					return BytesToUInt32(uncompsizeb);
				}
				set
				{
					UInt32ToBytes(value, uncompsizeb);
				}
			}

			public UInt16 Fnamelen
			{
				get
				{
					return BytesToUInt16(fnamelenb);
				}
				set
				{
					UInt16ToBytes(value, fnamelenb);
				}
			}

			public UInt16 Extralen
			{
				get
				{
					return BytesToUInt16(extralenb);
				}
				set
				{
					UInt16ToBytes(value, extralenb);
				}
			}

			public UInt16 Commentlen
			{
				get
				{
					return BytesToUInt16(commentlenb);
				}
				set
				{
					UInt16ToBytes(value, commentlenb);
				}
			}

			public UInt16 Disknum
			{
				get
				{
					return BytesToUInt16(disknumb);
				}
				set
				{
					UInt16ToBytes(value, disknumb);
				}
			}

			public UInt16 Inattr
			{
				get
				{
					return BytesToUInt16(inattrb);
				}
				set
				{
					UInt16ToBytes(value, inattrb);
				}
			}

			public UInt32 Outattr
			{
				get
				{
					return BytesToUInt32(outattrb);
				}
				set
				{
					UInt32ToBytes(value, outattrb);
				}
			}

			public UInt32 Headerpos
			{
				get
				{
					return BytesToUInt32(headerposb);
				}
				set
				{
					UInt32ToBytes(value, headerposb);
				}
			}

			public byte[] GetBytes()
			{
				List<byte> retByteList = new List<byte>();
				retByteList.AddRange(signatureb);
				retByteList.AddRange(madeverb);
				retByteList.AddRange(needverb);
				retByteList.AddRange(optb);
				retByteList.AddRange(comptypeb);
				retByteList.AddRange(filetimeb);
				retByteList.AddRange(filedateb);
				retByteList.AddRange(crc32b);
				retByteList.AddRange(compsizeb);
				retByteList.AddRange(uncompsizeb);
				retByteList.AddRange(fnamelenb);
				retByteList.AddRange(extralenb);
				retByteList.AddRange(commentlenb);
				retByteList.AddRange(disknumb);
				retByteList.AddRange(inattrb);
				retByteList.AddRange(outattrb);
				retByteList.AddRange(headerposb);
				if (Fnamelen != 0)
				{
					retByteList.AddRange(filenameb);
				}
				if (Extralen != 0)
				{
					retByteList.AddRange(extradatab);
				}
				if (Commentlen != 0)
				{
					retByteList.AddRange(commentb);
				}
				return retByteList.ToArray();
			}

			public PK0102Info()
			{
			}

			public PK0102Info(Stream inStream)
			{
				ShareMethodClass.StreamReadBuffer(inStream, signatureb);
				if (Signature != ZipArcClass.SIG_PK0102)
				{
					throw new InvalidDataException("PK0102 Signature");
				}
				ShareMethodClass.StreamReadBuffer(inStream, madeverb);
				ShareMethodClass.StreamReadBuffer(inStream, needverb);
				ShareMethodClass.StreamReadBuffer(inStream, optb);
				ShareMethodClass.StreamReadBuffer(inStream, comptypeb);
				ShareMethodClass.StreamReadBuffer(inStream, filetimeb);
				ShareMethodClass.StreamReadBuffer(inStream, filedateb);
				ShareMethodClass.StreamReadBuffer(inStream, crc32b);
				ShareMethodClass.StreamReadBuffer(inStream, compsizeb);
				ShareMethodClass.StreamReadBuffer(inStream, uncompsizeb);
				ShareMethodClass.StreamReadBuffer(inStream, fnamelenb);
				ShareMethodClass.StreamReadBuffer(inStream, extralenb);
				ShareMethodClass.StreamReadBuffer(inStream, commentlenb);
				ShareMethodClass.StreamReadBuffer(inStream, disknumb);
				ShareMethodClass.StreamReadBuffer(inStream, inattrb);
				ShareMethodClass.StreamReadBuffer(inStream, outattrb);
				ShareMethodClass.StreamReadBuffer(inStream, headerposb);
				if (Fnamelen == 0)
				{
					filenameb = Array.Empty<byte>();
                }
				else
				{
					filenameb = new byte[(int)Fnamelen];
					ShareMethodClass.StreamReadBuffer(inStream, filenameb);
				}
				if (Extralen == 0)
				{
					extradatab = Array.Empty<byte>();
                }
				else
				{
					extradatab = new byte[(int)Extralen];
					ShareMethodClass.StreamReadBuffer(inStream, extradatab);
				}
				if (Commentlen == 0)
				{
					commentb = Array.Empty<byte>();
                    return;
				}
				commentb = new byte[(int)Commentlen];
				ShareMethodClass.StreamReadBuffer(inStream, commentb);
			}
		}

        #endregion

        #region "PK0304 LocalHeader"

		/// <summary>
		/// LocalHeader(PK0304)クラス
		/// </summary>
		/// <remarks></remarks>
		internal class PK0304Info
		{
			private readonly byte[] signatureb = new byte[4];
			private readonly byte[] needverb = new byte[2];
			private readonly byte[] optb = new byte[2];
			private readonly byte[] comptypeb = new byte[2];
			private readonly byte[] filetimeb = new byte[2];
			private readonly byte[] filedateb = new byte[2];
			private readonly byte[] crc32b = new byte[4];
			private readonly byte[] compsizeb = new byte[4];
			private readonly byte[] uncompsizeb = new byte[4];
			private readonly byte[] fnamelenb = new byte[2];
			private readonly byte[] extralenb = new byte[2];
			public byte[] filenameb = null;
			public byte[] extradatab = null;
			public UInt32 Signature
			{
				get
				{
					return BytesToUInt32(signatureb);
				}
				set
				{
					UInt32ToBytes(value, signatureb);
				}
			}

			public UInt16 Needver
			{
				get
				{
					return BytesToUInt16(needverb);
				}
				set
				{
					UInt16ToBytes(value, needverb);
				}
			}

			public UInt16 Opt
			{
				get
				{
					return BytesToUInt16(optb);
				}
				set
				{
					UInt16ToBytes(value, optb);
				}
			}

			public UInt16 Comptype
			{
				get
				{
					return BytesToUInt16(comptypeb);
				}
				set
				{
					UInt16ToBytes(value, comptypeb);
				}
			}

			public UInt16 Filetime
			{
				get
				{
					return BytesToUInt16(filetimeb);
				}
				set
				{
					UInt16ToBytes(value, filetimeb);
				}
			}

			public UInt16 Filedate
			{
				get
				{
					return BytesToUInt16(filedateb);
				}
				set
				{
					UInt16ToBytes(value, filedateb);
				}
			}

			public UInt32 Crc32
			{
				get
				{
					return BytesToUInt32(crc32b);
				}
				set
				{
					UInt32ToBytes(value, crc32b);
				}
			}

			public UInt32 Compsize
			{
				get
				{
					return BytesToUInt32(compsizeb);
				}
				set
				{
					UInt32ToBytes(value, compsizeb);
				}
			}

			public UInt32 Uncompsize
			{
				get
				{
					return BytesToUInt32(uncompsizeb);
				}
				set
				{
					UInt32ToBytes(value, uncompsizeb);
				}
			}

			public UInt16 Fnamelen
			{
				get
				{
					return BytesToUInt16(fnamelenb);
				}
				set
				{
					UInt16ToBytes(value, fnamelenb);
				}
			}

			public UInt16 Extralen
			{
				get
				{
					return BytesToUInt16(extralenb);
				}
				set
				{
					UInt16ToBytes(value, extralenb);
				}
			}

			public byte[] GetBytes()
			{
				List<byte> retByteList = new List<byte>();
				retByteList.AddRange(signatureb);
				retByteList.AddRange(needverb);
				retByteList.AddRange(optb);
				retByteList.AddRange(comptypeb);
				retByteList.AddRange(filetimeb);
				retByteList.AddRange(filedateb);
				retByteList.AddRange(crc32b);
				retByteList.AddRange(compsizeb);
				retByteList.AddRange(uncompsizeb);
				retByteList.AddRange(fnamelenb);
				retByteList.AddRange(extralenb);
				if (Fnamelen != 0)
				{
					retByteList.AddRange(filenameb);
				}
				if (Extralen != 0)
				{
					retByteList.AddRange(extradatab);
				}
				return retByteList.ToArray();
			}

			public PK0304Info()
			{
			}

			public PK0304Info(Stream inStream)
			{
				ShareMethodClass.StreamReadBuffer(inStream, signatureb);
				if (Signature != ZipArcClass.SIG_PK0304)
				{
					throw new InvalidDataException("PK0304 Signature");
				}
				ShareMethodClass.StreamReadBuffer(inStream, needverb);
				ShareMethodClass.StreamReadBuffer(inStream, optb);
				ShareMethodClass.StreamReadBuffer(inStream, comptypeb);
				ShareMethodClass.StreamReadBuffer(inStream, filetimeb);
				ShareMethodClass.StreamReadBuffer(inStream, filedateb);
				ShareMethodClass.StreamReadBuffer(inStream, crc32b);
				ShareMethodClass.StreamReadBuffer(inStream, compsizeb);
				ShareMethodClass.StreamReadBuffer(inStream, uncompsizeb);
				ShareMethodClass.StreamReadBuffer(inStream, fnamelenb);
				ShareMethodClass.StreamReadBuffer(inStream, extralenb);
				if (Fnamelen == 0)
				{
					filenameb = Array.Empty<byte>();
				}
				else
				{
					filenameb = new byte[(int)Fnamelen];
					ShareMethodClass.StreamReadBuffer(inStream, filenameb);
				}
				if (Extralen == 0)
				{
					extradatab = Array.Empty<byte>();
                    return;
				}
				extradatab = new byte[(int)Extralen];
				ShareMethodClass.StreamReadBuffer(inStream, extradatab);
			}

		}

        #endregion

        #region "PK0506 EndOfCentralDirectory"

		/// <summary>
		/// EndOfCentralDirectory(PK0506)クラス
		/// </summary>
		/// <remarks></remarks>
		internal class PK0506Info
		{
			private readonly byte[] signatureb = new byte[4];
			private readonly byte[] disknumb = new byte[2];
			private readonly byte[] startdisknumb = new byte[2];
			private readonly byte[] diskdirentryb = new byte[2];
			private readonly byte[] direntryb = new byte[2];
			private readonly byte[] dirsizeb = new byte[4];
			private readonly byte[] startposb = new byte[4];
			private readonly byte[] commentlenb = new byte[2];
			private byte[] commentb = null;

			public UInt32 Signature
			{
				get
				{
					return BytesToUInt32(signatureb);
				}
				set
				{
					UInt32ToBytes(value, signatureb);
				}
			}

			public UInt16 Disknum
			{
				get
				{
					return BytesToUInt16(disknumb);
				}
				set
				{
					UInt16ToBytes(value, disknumb);
				}
			}

			public UInt16 Startdisknum
			{
				get
				{
					return BytesToUInt16(startdisknumb);
				}
				set
				{
					UInt16ToBytes(value, startdisknumb);
				}
			}

			public UInt16 Diskdirentry
			{
				get
				{
					return BytesToUInt16(diskdirentryb);
				}
				set
				{
					UInt16ToBytes(value, diskdirentryb);
				}
			}

			public UInt16 Direntry
			{
				get
				{
					return BytesToUInt16(direntryb);
				}
				set
				{
					UInt16ToBytes(value, direntryb);
				}
			}

			public UInt32 Dirsize
			{
				get
				{
					return BytesToUInt32(dirsizeb);
				}
				set
				{
					UInt32ToBytes(value, dirsizeb);
				}
			}

			public UInt32 Startpos
			{
				get
				{
					return BytesToUInt32(startposb);
				}
				set
				{
					UInt32ToBytes(value, startposb);
				}
			}

			public UInt16 Commentlen
			{
				get
				{
					return BytesToUInt16(commentlenb);
				}
				//set
				//{
				//	UInt16ToBytes(value, commentlenb);
				//}
			}

			public byte[] Comment {
                get
                {
                    return this.commentb;
                }
                set
                {
                    if (value == null)
                    {
                        UInt16ToBytes(0, commentlenb);
						this.commentb = null;
						return;
					}
					if (value.Length > UInt16.MaxValue)
					{
						UInt16ToBytes(UInt16.MaxValue, commentlenb);
						byte[] buf = new byte[UInt16.MaxValue];
						Array.Copy(value, buf, UInt16.MaxValue);
						this.commentb = buf;

					}
					else
					{
                        UInt16ToBytes((ushort)value.Length, commentlenb);
						this.commentb = value;
                    }
                }
            }

            public byte[] GetBytes()
			{
				List<byte> retByteList = new List<byte>();
				retByteList.AddRange(signatureb);
				retByteList.AddRange(disknumb);
				retByteList.AddRange(startdisknumb);
				retByteList.AddRange(diskdirentryb);
				retByteList.AddRange(direntryb);
				retByteList.AddRange(dirsizeb);
				retByteList.AddRange(startposb);
				retByteList.AddRange(commentlenb);
				if (Commentlen != 0)
				{
					retByteList.AddRange(commentb);
				}
				return retByteList.ToArray();
			}

			public PK0506Info()
			{
			}

			public PK0506Info(Stream inStream)
			{
				ShareMethodClass.StreamReadBuffer(inStream, signatureb);
				if (Signature != ZipArcClass.SIG_PK0506)
				{
					throw new InvalidDataException("PK0506 Signature");
				}
				ShareMethodClass.StreamReadBuffer(inStream, disknumb);
				ShareMethodClass.StreamReadBuffer(inStream, startdisknumb);
				ShareMethodClass.StreamReadBuffer(inStream, diskdirentryb);
				ShareMethodClass.StreamReadBuffer(inStream, direntryb);
				ShareMethodClass.StreamReadBuffer(inStream, dirsizeb);
				ShareMethodClass.StreamReadBuffer(inStream, startposb);
				ShareMethodClass.StreamReadBuffer(inStream, commentlenb);
				if (Commentlen == 0)
				{
					commentb = null;
					return;
				}
				commentb = new byte[(int)Commentlen];
				ShareMethodClass.StreamReadBuffer(inStream, commentb);
			}

		}

        #endregion

        #region "PK0606 Zip64EndOfCentralDirectory"

		/// <summary>
		/// Zip64EndOfCentralDirectory(PK0606)クラス
		/// </summary>
		/// <remarks></remarks>
		internal class PK0606Info
		{
			private readonly byte[] signatureb = new byte[4];
			private readonly byte[] headersizeb = new byte[8];
			private readonly byte[] madeverb = new byte[2];
			private readonly byte[] needverb = new byte[2];
			private readonly byte[] disknumb = new byte[4];
			private readonly byte[] startdisknumb = new byte[4];
			private readonly byte[] diskdirentryb = new byte[8];
			private readonly byte[] direntryb = new byte[8];
			private readonly byte[] dirsizeb = new byte[8];
			private readonly byte[] startposb = new byte[8];
			public byte[] zip64extdatab = null;

			public UInt32 Signature
			{
				get
				{
					return BytesToUInt32(signatureb);
				}
				set
				{
					UInt32ToBytes(value, signatureb);
				}
			}

			public UInt16 Madever
			{
				get
				{
					return BytesToUInt16(madeverb);
				}
				set
				{
					UInt16ToBytes(value, madeverb);
				}
			}

			public UInt16 Needver
			{
				get
				{
					return BytesToUInt16(needverb);
				}
				set
				{
					UInt16ToBytes(value, needverb);
				}
			}

			public UInt32 Disknum
			{
				get
				{
					return BytesToUInt32(disknumb);
				}
				set
				{
					UInt32ToBytes(value, disknumb);
				}
			}

			public UInt32 Startdisknum
			{
				get
				{
					return BytesToUInt32(startdisknumb);
				}
				set
				{
					UInt32ToBytes(value, startdisknumb);
				}
			}

			public UInt64 Diskdirentry
			{
				get
				{
					return BytesToUInt64(diskdirentryb);
				}
				set
				{
					UInt64ToBytes(value, diskdirentryb);
				}
			}

			public UInt64 Direntry
			{
				get
				{
					return BytesToUInt64(direntryb);
				}
				set
				{
					UInt64ToBytes(value, direntryb);
				}
			}

			public UInt64 Dirsize
			{
				get
				{
					return BytesToUInt64(dirsizeb);
				}
				set
				{
					UInt64ToBytes(value, dirsizeb);
				}
			}

			public UInt64 Startpos
			{
				get
				{
					return BytesToUInt64(startposb);
				}
				set
				{
					UInt64ToBytes(value, startposb);
				}
			}

			private UInt64 CalcBaseLength()
			{
				return (ulong)(madeverb.Length + 
					           needverb.Length + 
							   disknumb.Length + 
							   startdisknumb.Length + 
							   diskdirentryb.Length + 
							   direntryb.Length + 
							   dirsizeb.Length + 
							   startposb.Length);
			}

			private UInt64 CalcHeaderLength()
			{
				UInt64 extLength = ((zip64extdatab != null) ? (UInt64)zip64extdatab.Length : 0u);
				return this.CalcBaseLength() + extLength;
			}

			public byte[] GetBytes()
			{
				UInt64ToBytes(CalcHeaderLength(), headersizeb);
				List<byte> retByteList = new List<byte>();
				retByteList.AddRange(signatureb);
				retByteList.AddRange(headersizeb);
				retByteList.AddRange(madeverb);
				retByteList.AddRange(needverb);
				retByteList.AddRange(disknumb);
				retByteList.AddRange(startdisknumb);
				retByteList.AddRange(diskdirentryb);
				retByteList.AddRange(direntryb);
				retByteList.AddRange(dirsizeb);
				retByteList.AddRange(startposb);
				if (zip64extdatab != null && zip64extdatab.Length != 0)
				{
					retByteList.AddRange(zip64extdatab);
				}
				return retByteList.ToArray();
			}

			public PK0606Info()
			{
			}

			public PK0606Info(Stream inStream)
			{
				ShareMethodClass.StreamReadBuffer(inStream, signatureb);
				if (Signature != ZipArcClass.SIG_PK0606)
				{
					throw new InvalidDataException("PK0606 Signature");
				}
				ShareMethodClass.StreamReadBuffer(inStream, headersizeb);
				ShareMethodClass.StreamReadBuffer(inStream, madeverb);
				ShareMethodClass.StreamReadBuffer(inStream, needverb);
				ShareMethodClass.StreamReadBuffer(inStream, disknumb);
				ShareMethodClass.StreamReadBuffer(inStream, startdisknumb);
				ShareMethodClass.StreamReadBuffer(inStream, diskdirentryb);
				ShareMethodClass.StreamReadBuffer(inStream, direntryb);
				ShareMethodClass.StreamReadBuffer(inStream, dirsizeb);
				ShareMethodClass.StreamReadBuffer(inStream, startposb);
				ulong extLength = BitConverter.ToUInt64(headersizeb, 0) - CalcBaseLength();
				if (extLength == 0)
				{
					zip64extdatab = null;
					return;
				}
				zip64extdatab = new byte[(int)extLength];
				ShareMethodClass.StreamReadBuffer(inStream, zip64extdatab);
			}

		}

        #endregion

        #region "PK0607 Zip64EndOfCentralDirectoryLocator"

		/// <summary>
		/// Zip64EndOfCentralDirectoryLocator(PK0607)クラス
		/// </summary>
		/// <remarks></remarks>
		internal class PK0607Info
		{
			private readonly byte[] signatureb = new byte[4];
			private readonly byte[] startdisknumb = new byte[4];
			private readonly byte[] startposb = new byte[8];
			private readonly byte[] totaldisksb = new byte[4];

			public UInt32 Signature
			{
				get
				{
					return BytesToUInt32(signatureb);
				}
				set
				{
					UInt32ToBytes(value, signatureb);
				}
			}

			public UInt32 Startdisknum
			{
				get
				{
					return BytesToUInt32(startdisknumb);
				}
				set
				{
					UInt32ToBytes(value, startdisknumb);
				}
			}

			public UInt64 Startpos
			{
				get
				{
					return BytesToUInt64(startposb);
				}
				set
				{
					UInt64ToBytes(value, startposb);
				}
			}

			public UInt32 Totaldisks
			{
				get
				{
					return BytesToUInt32(totaldisksb);
				}
				set
				{
					UInt32ToBytes(value, totaldisksb);
				}
			}

			public byte[] GetBytes()
			{
				List<byte> retByteList = new List<byte>();
				retByteList.AddRange(signatureb);
				retByteList.AddRange(startdisknumb);
				retByteList.AddRange(startposb);
				retByteList.AddRange(totaldisksb);
				return retByteList.ToArray();
			}

			public PK0607Info()
			{
			}

			public PK0607Info(Stream inStream)
			{
				ShareMethodClass.StreamReadBuffer(inStream, signatureb);
				if (Signature != ZipArcClass.SIG_PK0607)
				{
					throw new InvalidDataException("PK0607 Signature");
				}
				ShareMethodClass.StreamReadBuffer(inStream, startdisknumb);
				ShareMethodClass.StreamReadBuffer(inStream, startposb);
				ShareMethodClass.StreamReadBuffer(inStream, totaldisksb);
			}

		}

        #endregion

        #region "PK0708 DataDescriptor"

        /// <summary>
        /// DataDescriptor(PK0708)クラス
        /// </summary>
        /// <remarks></remarks>
        internal class PK0708Info
		{
			private readonly byte[] signatureb = new byte[4];
			private readonly byte[] crc32b = new byte[4];
			private readonly byte[] compsizeb = new byte[4];
			private readonly byte[] uncompsizeb = new byte[4];
			private long compsize64;
			private long uncompsize64;

			public UInt32 Signature
			{
				get
				{
					return BytesToUInt32(signatureb);
				}
				set
				{
					UInt32ToBytes(value, signatureb);
				}
			}

			public UInt32 Crc32
			{
				get
				{
					return BytesToUInt32(crc32b);
				}
				set
				{
					UInt32ToBytes(value, crc32b);
				}
			}

			public UInt32 Compsize
            {
				get {
					return BytesToUInt32(compsizeb);
				}
			}

			public UInt32 Uncompsize
            {
                get
                {
                    return BytesToUInt32(uncompsizeb);
				}
            }

			public long CompsizeLong
			{
				get
				{
					return compsize64;
				}
				set
				{
					compsize64 = value;
					if (value >= uint.MaxValue)
					{
						UInt32ToBytes(uint.MaxValue, compsizeb);
					}
					else
					{
						UInt32ToBytes((uint)value, compsizeb);
					}
				}
			}

			public long UncompsizeLong
			{
				get
				{
					return uncompsize64;
				}
				set
				{
					uncompsize64 = value;
					if (value >= uint.MaxValue)
					{
						UInt32ToBytes(UInt32.MaxValue, uncompsizeb);
					}
					else
					{
						UInt32ToBytes((UInt32)value, uncompsizeb);
					}
				}
			}

			public byte[] GetBytes()
			{
				List<byte> retByteList = new List<byte>();
				retByteList.AddRange(signatureb);
				retByteList.AddRange(crc32b);
				retByteList.AddRange(compsizeb);
				retByteList.AddRange(uncompsizeb);
				return retByteList.ToArray();
			}

			public PK0708Info()
			{
			}

			public PK0708Info(Stream inStream)
			{
				ShareMethodClass.StreamReadBuffer(inStream, signatureb);
				if (Signature != ZipArcClass.SIG_PK0708)
				{
					throw new InvalidDataException("PK0708 Signature");
				}
				ShareMethodClass.StreamReadBuffer(inStream, crc32b);
				ShareMethodClass.StreamReadBuffer(inStream, compsizeb);
				ShareMethodClass.StreamReadBuffer(inStream, uncompsizeb);
			}

		}

        #endregion

        #region AES extra data field(9901)

		/// <summary>
		/// AES extra data field
		/// </summary>
		/// <remarks></remarks>
		internal class AesExtraDataInfo
		{
			private readonly byte[] extraHeaderb = new byte[2] { 0x01, 0x99 };
			private readonly byte[] dataSizeb = new byte[2] { 0x07, 0 };
			private readonly byte[] versionNumberb = new byte[2] { 0x02, 0 };
			private readonly byte[] venderIdb = new byte[2] { 0x41, 0x45 };
			private readonly byte[] encStrengthb = new byte[1];
			private readonly byte[] comptypeb = new byte[2];

			public ushort VersionNumber
			{
				get
				{
					return BytesToUInt16(versionNumberb);
				}
				set
				{
					UInt16ToBytes(value, versionNumberb);
				}
			}

			public ushort EncStrength
			{
				get
				{
					return encStrengthb[0];
				}
				set
				{
					encStrengthb[0] = (byte)value;
				}
			}

			public ushort Comptype
			{
				get
				{
					return BytesToUInt16(comptypeb);
				}
				set
				{
					UInt16ToBytes(value, comptypeb);
				}
			}

			public byte[] GetBytes()
			{
				List<byte> retByteList = new List<byte>();
				retByteList.AddRange(extraHeaderb);
				retByteList.AddRange(dataSizeb);
				retByteList.AddRange(versionNumberb);
				retByteList.AddRange(venderIdb);
				retByteList.AddRange(encStrengthb);
				retByteList.AddRange(comptypeb);
				return retByteList.ToArray();
			}

			public AesExtraDataInfo()
			{
			}

			public AesExtraDataInfo(Stream inStream)
			{
				ShareMethodClass.StreamReadBuffer(inStream, extraHeaderb);
				ShareMethodClass.StreamReadBuffer(inStream, dataSizeb);
				ShareMethodClass.StreamReadBuffer(inStream, versionNumberb);
				ShareMethodClass.StreamReadBuffer(inStream, venderIdb);
				ShareMethodClass.StreamReadBuffer(inStream, encStrengthb);
				ShareMethodClass.StreamReadBuffer(inStream, comptypeb);
			}

		}

        #endregion

        #region NTFS FileTimestamp Extra Data Field(000a)

		/// <summary>
		/// NTFS FileTimestamp Extra Data Field
		/// </summary>
		/// <remarks></remarks>
		internal class NtfsDateExtraDataInfo
		{
			private readonly byte[] extraHeaderb = new byte[2] { 0x0a, 0x00 };
			private readonly byte[] dataSizeb = new byte[2] { 0x20, 0x00 };
			private readonly byte[] reservedb = new byte[4];
			private readonly byte[] tagb = new byte[2] { 0x01, 0x00 };
			private readonly byte[] attrSizeb = new byte[2] { 0x18, 0x00 };
			private readonly byte[] modifyTimeb = new byte[8];
			private readonly byte[] accessTimeb = new byte[8];
			private readonly byte[] createTimeb = new byte[8];

			public long ModifyTime
			{
				get
				{
					return BytesToInt64(modifyTimeb);
				}
				set
				{
					Int64ToBytes(value, modifyTimeb);
				}
			}

			public long AccessTime
			{
				get
				{
					return BytesToInt64(accessTimeb);
				}
				set
				{
					Int64ToBytes(value, accessTimeb);
				}
			}

			public long CreateTime
			{
				get
				{
					return BytesToInt64(createTimeb);
				}
				set
				{
					Int64ToBytes(value, createTimeb);
				}
			}

			public byte[] GetBytes()
			{
				List<byte> retByteList = new List<byte>();
				retByteList.AddRange(extraHeaderb);
				retByteList.AddRange(dataSizeb);
				retByteList.AddRange(reservedb);
				retByteList.AddRange(tagb);
				retByteList.AddRange(attrSizeb);
				retByteList.AddRange(modifyTimeb);
				retByteList.AddRange(accessTimeb);
				retByteList.AddRange(createTimeb);
				return retByteList.ToArray();
			}

			public NtfsDateExtraDataInfo()
			{
			}

			public NtfsDateExtraDataInfo(Stream inStream)
			{
				ShareMethodClass.StreamReadBuffer(inStream, extraHeaderb);
				ShareMethodClass.StreamReadBuffer(inStream, dataSizeb);
				ShareMethodClass.StreamReadBuffer(inStream, reservedb);
				ShareMethodClass.StreamReadBuffer(inStream, tagb);
				ShareMethodClass.StreamReadBuffer(inStream, attrSizeb);
				ShareMethodClass.StreamReadBuffer(inStream, modifyTimeb);
				ShareMethodClass.StreamReadBuffer(inStream, accessTimeb);
				ShareMethodClass.StreamReadBuffer(inStream, createTimeb);
			}

		}

        #endregion

        #region Zip64 Extra Data Field(0001)

		/// <summary>
		/// Zip64 Extra Data Field
		/// </summary>
		/// <remarks></remarks>
		internal class Zip64ExtraDataInfo
		{
			private readonly byte[] extraHeaderb = new byte[2] { 0x01, 0x00 };
			private readonly byte[] datasizeb = new byte[2] { 0x10, 0x00 };
			private readonly byte[] uncompsizeb = new byte[8];
			private readonly byte[] compsizeb = new byte[8];
			private readonly byte[] headerOffsetb = new byte[8];
			private readonly byte[] diskNumb = new byte[4];

			public ushort Datasize
			{
				get
				{
					return BytesToUInt16(datasizeb);
				}
				private set
				{
					UInt16ToBytes(value, datasizeb);
				}
			}

			public ulong Uncompsize
			{
				get
				{
					return BytesToUInt64(uncompsizeb);
				}
				set
				{
					UInt64ToBytes(value, uncompsizeb);
				}
			}

			public ulong Compsize
			{
				get
				{
					return BytesToUInt64(compsizeb);
				}
				set
				{
					UInt64ToBytes(value, compsizeb);
				}
			}

			public ulong HeaderOffset
			{
				get
				{
					return BytesToUInt64(headerOffsetb);
				}
				set
				{
					UInt64ToBytes(value, headerOffsetb);
				}
			}

			public uint DiskNum
			{
				get
				{
					return BytesToUInt32(diskNumb);
				}
				set
				{
					UInt32ToBytes(value, diskNumb);
				}
			}

			public byte[] GetCentralDirectoryBytes()
			{

                List<byte> retByteList = new List<byte>();
                retByteList.AddRange(extraHeaderb);

				// Length設定
				List<byte> buf = new List<byte>();

				// 未圧縮サイズ
				if (Uncompsize >= uint.MaxValue)
				{
					buf.AddRange(uncompsizeb);
				}

				// 圧縮後サイズ
				if(Compsize >= uint.MaxValue)
				{
					buf.AddRange(compsizeb);
				}

				// ヘッダーオフセット
				if(HeaderOffset >= uint.MaxValue)
				{
					buf.AddRange(headerOffsetb);
				}

                Datasize = (ushort)buf.Count;
                retByteList.AddRange(datasizeb);
                retByteList.AddRange(buf);

				return retByteList.ToArray();
			}

            public byte[] GetLocalHeaderBytes()
            {

                List<byte> retByteList = new List<byte>();
                retByteList.AddRange(extraHeaderb);

                // Length設定
                List<byte> buf = new List<byte>();

                // 未圧縮サイズ・圧縮後サイズ
                if (Uncompsize >= uint.MaxValue || Compsize >= uint.MaxValue)
                {
                    buf.AddRange(uncompsizeb);
                    buf.AddRange(compsizeb);
                }

                Datasize = (ushort)buf.Count;
                retByteList.AddRange(datasizeb);
                retByteList.AddRange(buf);

                return retByteList.ToArray();
            }


            public Zip64ExtraDataInfo()
			{
			}

            public Zip64ExtraDataInfo(Stream inStream, uint hdUncompsize, uint hdCompsize, uint hdOffset)
            {

				ShareMethodClass.StreamReadBuffer(inStream, extraHeaderb);
				ShareMethodClass.StreamReadBuffer(inStream, datasizeb);

				int zip64Size = Datasize;

				// 未圧縮サイズ
                if (zip64Size < 8) return;
                if (hdUncompsize == uint.MaxValue)
                {
                    zip64Size -= 8;
                    ShareMethodClass.StreamReadBuffer(inStream, uncompsizeb);
                }

				// 圧縮後サイズ
                if (zip64Size < 8) return;
                if (hdCompsize == uint.MaxValue)
                {
                    zip64Size -= 8;
                    ShareMethodClass.StreamReadBuffer(inStream, compsizeb);
                }

				// ヘッダーオフセット
                if (zip64Size < 8) return;
                if (hdOffset == uint.MaxValue)
                {
                    zip64Size -= 8;
                    ShareMethodClass.StreamReadBuffer(inStream, headerOffsetb);
                }

				// DiskNum
                if (zip64Size < 4) return;
                ShareMethodClass.StreamReadBuffer(inStream, diskNumb);

                //            ShareMethodClass.StreamReadBuffer(inStream, extraHeaderb);
                //ShareMethodClass.StreamReadBuffer(inStream, datasizeb);

                //            if ((uint)Datasize == 8u)
                //{
                //                ShareMethodClass.StreamReadBuffer(inStream, headerOffsetb);
                //	return;
                //            }
                //if ((uint)Datasize >= 16u)
                //            {
                //                ShareMethodClass.StreamReadBuffer(inStream, uncompsizeb);
                //                ShareMethodClass.StreamReadBuffer(inStream, compsizeb);
                //}
                //if ((uint)Datasize >= 24u)
                //{
                //	ShareMethodClass.StreamReadBuffer(inStream, headerOffsetb);
                //}
                //if ((uint)Datasize >= 28u)
                //{
                //	ShareMethodClass.StreamReadBuffer(inStream, diskNumb);
                //}
            }

            //public Zip64ExtraDataInfo(Stream inStream, int length)
            //         {
            //	ShareMethodClass.StreamReadBuffer(inStream, extraHeaderb);
            //          	ShareMethodClass.StreamReadBuffer(inStream, datasizeb);
            //             if ((uint)Datasize == 8) {
            //                 ShareMethodClass.StreamReadBuffer(inStream, headerOffsetb);
            //                 return;
            //	}
            //             if ((uint)Datasize >= 16u)
            //	{ 
            //		ShareMethodClass.StreamReadBuffer(inStream, uncompsizeb);
            //		ShareMethodClass.StreamReadBuffer(inStream, compsizeb);
            //             }
            //             if ((uint)Datasize >= 24u)
            //	{
            //		ShareMethodClass.StreamReadBuffer(inStream, headerOffsetb);
            //	}
            //	if ((uint)Datasize >= 28u)
            //	{
            //		ShareMethodClass.StreamReadBuffer(inStream, diskNumb);
            //	}
            //}
        }

        #endregion

        #region extended timestamp(5455)

		/// <summary>
		/// Extended Timestamp Field
		/// </summary>
		/// <remarks></remarks>
		internal class ExtendedTimestampInfo
		{
			private readonly byte[] extraHeaderb = new byte[2] { 0x55, 0x54 };
            private readonly byte[] dataSizeb = new byte[2] { 0x0d, 0x00 };
            private readonly byte[] flagb = new byte[1] { 0x07 };
            internal readonly byte[] modifyTimeb = new byte[4];
            internal readonly byte[] accessTimeb = new byte[4];
            internal readonly byte[] createTimeb = new byte[4];

            private ushort Datasize
            {
                get
                {
                    return BytesToUInt16(dataSizeb);
                }
            }

            public byte Flag
            {
                get
                {
                    return flagb[0];
                }
            }

            public DateTime ModifyTime
            {
                get
                {
                    int sec = BytesToInt32(modifyTimeb);
					return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sec);
                }
				set
				{
					TimeSpan s = value - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
					Int32ToBytes((Int32)s.TotalSeconds, modifyTimeb);
				}
            }

            public DateTime AccessTime
            {
                get
                {
                    int sec = BytesToInt32(accessTimeb);
                    return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sec);
                }
                set
                {
                    TimeSpan s = value - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    Int32ToBytes((Int32)s.TotalSeconds, accessTimeb);
                }
            }

            public DateTime CreateTime
            {
                get
                {
                    int sec = BytesToInt32(createTimeb);
                    return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sec);
                }
                set
                {
                    TimeSpan s = value - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    Int32ToBytes((Int32)s.TotalSeconds, createTimeb);
                }
            }

            public ExtendedTimestampInfo() { 
			}

            public ExtendedTimestampInfo(Stream inStream)
            {
                ShareMethodClass.StreamReadBuffer(inStream, extraHeaderb);
                ShareMethodClass.StreamReadBuffer(inStream, dataSizeb);
                ShareMethodClass.StreamReadBuffer(inStream, flagb);

                // Modify Timestampは必ず存在しそうだけど念のためなくても良いように実装しておく
                bool modExists = false;
                bool accExists = false;
                int readLen = 1;

                // Modify Time
                if ((flagb[0] & 0x01) == 0x01)
                {
                    ShareMethodClass.StreamReadBuffer(inStream, modifyTimeb);
                    modExists = true;
                    readLen += 4;
                    if (this.Datasize <= readLen)
                    {
                        modifyTimeb.CopyTo(accessTimeb, 0);
                        modifyTimeb.CopyTo(createTimeb, 0);
                        return;
                    }
                }

                // Access Time
                if ((flagb[0] & 0x02) == 0x02)
                {
                    ShareMethodClass.StreamReadBuffer(inStream, accessTimeb);
                    accExists = true;
                    readLen += 4;
                    if (modExists == false) accessTimeb.CopyTo(modifyTimeb, 0);
                    if (this.Datasize <= readLen)
                    {
                        modifyTimeb.CopyTo(createTimeb, 0);
                        return;
                    }
                }
                else if (modExists == true) modifyTimeb.CopyTo(accessTimeb, 0);

                // Create Time
                if ((flagb[0] & 0x04) == 0x04)
                {
                    ShareMethodClass.StreamReadBuffer(inStream, createTimeb);

                    if (modExists == false) createTimeb.CopyTo(modifyTimeb, 0);
                    if (accExists == false) createTimeb.CopyTo(accessTimeb, 0);
                }
                else modifyTimeb.CopyTo(createTimeb, 0);

            }

            public byte[] GetBytes(bool localHeader)
            {
                List<byte> retByteList = new List<byte>();
				if (localHeader)
				{
                    // LocalHeader
                    retByteList.AddRange(extraHeaderb);
                    retByteList.AddRange(dataSizeb);
                    retByteList.AddRange(flagb);
                    retByteList.AddRange(modifyTimeb);
                    retByteList.AddRange(accessTimeb);
                    retByteList.AddRange(createTimeb);
                }
                else
				{
                    // CentralDirectory
                    retByteList.AddRange(extraHeaderb);
                    byte[] cdSizeb = new byte[2] { 0x05, 0x00 };
                    retByteList.AddRange(cdSizeb);
                    retByteList.AddRange(flagb);
                    retByteList.AddRange(modifyTimeb);
                }
                return retByteList.ToArray();
            }


        }


        #endregion

        #region Info-ZIP Unicode Path Extra Field(7075)

		/// <summary>
		/// Info-ZIP Unicode Path Extra Field
		/// </summary>
		/// <remarks></remarks>
		internal class UnicodePathExtraDataField
        {
            private readonly byte[] extraHeaderb = new byte[2] { 0x75, 0x70 };
            private readonly byte[] datasizeb = new byte[2];
            private readonly byte[] versionb = new byte[1] { 0x01 };
            private readonly byte[] nameCrc32b = new byte[4];
			private byte[] unicodeNameb = null;
            private readonly uint headerNameCrc32;
            private string unicodeName = null;
            private readonly string unicodeNameOrg = null;

            public byte Version
            {
                get
                {
                    return versionb[0];
                }
                set
                {
					versionb[0] = value;
                }
            }

            private ushort Datasize
            {
                get
                {
                    return BytesToUInt16(datasizeb);
                }
				set
				{
                    UInt16ToBytes(value, datasizeb);
                }
            }

            public uint NameCrc32
            {
                get
                {
                    return BytesToUInt32(nameCrc32b);
                }
                set
                {
                    UInt32ToBytes(value, nameCrc32b);
                }
            }
            public uint HeaderNameCrc32
            {
                get
                {
					return headerNameCrc32;
                }
            }

            public string UnicodeName
            {
                get
                {
					return unicodeName;
                }
                set
                {
					unicodeName = value;
                    unicodeNameb = ShareMethodClass.EncodingGetBytes(unicodeName, ShareMethodClass.Utf8Encoding);
                }
            }

            public byte[] UnicodeNameB
            {
                get
                {
                    return unicodeNameb;
                }
            }

            public string UnicodeNameOrg
            {
                get
                {
                    return unicodeNameOrg;
                }
            }

            public byte[] GetBytes()
            {
				// ファイル名変換

				Datasize = (ushort)(5 + unicodeNameb.Length);
                List<byte> retByteList = new List<byte>();
                retByteList.AddRange(extraHeaderb);
                retByteList.AddRange(datasizeb);
                retByteList.AddRange(versionb);
                retByteList.AddRange(nameCrc32b);
                retByteList.AddRange(unicodeNameb);
                return retByteList.ToArray();
            }

            public UnicodePathExtraDataField()
            {
            }

            public UnicodePathExtraDataField(Stream inStream, string headerFileName, byte[] headerFileNameb)
            {
                ShareMethodClass.StreamReadBuffer(inStream, extraHeaderb);
                ShareMethodClass.StreamReadBuffer(inStream, datasizeb);
                ShareMethodClass.StreamReadBuffer(inStream, versionb);
                ShareMethodClass.StreamReadBuffer(inStream, nameCrc32b);
                
				unicodeNameb = new byte[Datasize - 5];
                ShareMethodClass.StreamReadBuffer(inStream, unicodeNameb);
                unicodeNameOrg = ShareMethodClass.EncodingGetString(unicodeNameb, ShareMethodClass.Utf8Encoding);
                // CrcCheck
                headerNameCrc32 = CalcCrc32Stream.CalcCrc(headerFileNameb);
                if (NameCrc32 == headerNameCrc32)
				{
                    unicodeName = unicodeNameOrg;
                }
				else
				{
					unicodeName = headerFileName;
				}
            }
        }

        #endregion

        #region "Private Methods"

        private static UInt16 BytesToUInt16(byte[] value)
        {
			return BitConverter.ToUInt16(value, 0);
		}

		private static UInt32 BytesToUInt32(byte[] value)
        {
			return BitConverter.ToUInt32(value, 0);
		}

        private static Int32 BytesToInt32(byte[] value)
        {
			return BitConverter.ToInt32(value, 0);
        }

        private static UInt64 BytesToUInt64(byte[] value)
        {
			return BitConverter.ToUInt64(value, 0);
		}

		private static Int64 BytesToInt64(byte[] value)
		{
			return BitConverter.ToInt64(value, 0);
		}

		private static void UInt16ToBytes(UInt16 value, byte[] buf)
        {
			UInt16 work = value;
			buf[0] = (byte)(work & 0xff);
			work >>= 8;
			buf[1] = (byte)work;
		}
		private static void UInt32ToBytes(UInt32 value, byte[] buf)
        {
			UInt32 work = value;
			buf[0] = (byte)(work & 0xff);
			work >>= 8;
			buf[1] = (byte)(work & 0xff);
			work >>= 8;
			buf[2] = (byte)(work & 0xff);
			work >>= 8;
			buf[3] = (byte)work;
		}
        private static void Int32ToBytes(Int32 value, byte[] buf)
        {
            Int32 work = value;
            buf[0] = (byte)(work & 0xff);
            work >>= 8;
            buf[1] = (byte)(work & 0xff);
            work >>= 8;
            buf[2] = (byte)(work & 0xff);
            work >>= 8;
            buf[3] = (byte)work;
        }
        private static void UInt64ToBytes(UInt64 value, byte[] buf)
        {
			UInt64 work = value;
			buf[0] = (byte)(work & 0xff);
			work >>= 8;
			buf[1] = (byte)(work & 0xff);
			work >>= 8;
			buf[2] = (byte)(work & 0xff);
			work >>= 8;
			buf[3] = (byte)(work & 0xff);
			work >>= 8;
			buf[4] = (byte)(work & 0xff);
			work >>= 8;
			buf[5] = (byte)(work & 0xff);
			work >>= 8;
			buf[6] = (byte)(work & 0xff);
			work >>= 8;
			buf[7] = (byte)work;
		}

		private static void Int64ToBytes(Int64 value, byte[] buf)
		{
			// マイナスは発生し得ないのでUInt64で処理する
			UInt64ToBytes((UInt64)value, buf);
		}

        #endregion

    }
}
