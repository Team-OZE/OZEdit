using StormLibSharp.Native;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace StormLibSharp
{
    public class MpqArchive : IDisposable
    {
        private MpqArchiveSafeHandle _handle;
        private List<MpqFileStream> _openFiles = new List<MpqFileStream>();
        private FileAccess _accessType;
        private List<MpqArchiveCompactingEventHandler> _compactCallbacks = new List<MpqArchiveCompactingEventHandler>();
        private SFILE_COMPACT_CALLBACK _compactCallback;

        #region Constructors / Factories
        public MpqArchive(string filePath, FileAccess accessType)
        {
            _accessType = accessType;
            SFileOpenArchiveFlags flags = SFileOpenArchiveFlags.TypeIsFile;
            if (accessType == FileAccess.Read)
                flags |= SFileOpenArchiveFlags.AccessReadOnly;
            else
                flags |= SFileOpenArchiveFlags.AccessReadWriteShare;

            // constant 2 = SFILE_OPEN_HARD_DISK_FILE
            if (!NativeMethods.SFileOpenArchive(filePath, 2, flags, out _handle))
                throw new Win32Exception(); // Implicitly calls GetLastError
        }

        public MpqArchive(MemoryMappedFile file, FileAccess accessType)
        {
            _accessType = accessType;
            string fileName = Win32Methods.GetFileNameOfMemoryMappedFile(file);
            if (fileName == null)
                throw new ArgumentException("Could not retrieve the name of the file to initialize.");

            SFileOpenArchiveFlags flags = SFileOpenArchiveFlags.TypeIsMemoryMapped;
            if (accessType == FileAccess.Read)
                flags |= SFileOpenArchiveFlags.AccessReadOnly;
            else
                flags |= SFileOpenArchiveFlags.AccessReadWriteShare;

            // constant 2 = SFILE_OPEN_HARD_DISK_FILE
            if (!NativeMethods.SFileOpenArchive(fileName, 2, flags, out _handle))
                throw new Win32Exception(); // Implicitly calls GetLastError
        }

        private MpqArchive(string filePath, MpqArchiveVersion version, MpqFileStreamAttributes listfileAttributes, MpqFileStreamAttributes attributesFileAttributes, int maxFileCount)
        {
            if (maxFileCount < 0)
                throw new ArgumentException("maxFileCount");

            SFileOpenArchiveFlags flags = SFileOpenArchiveFlags.TypeIsFile | SFileOpenArchiveFlags.AccessReadWriteShare;
            flags |= (SFileOpenArchiveFlags)version;

            //SFILE_CREATE_MPQ create = new SFILE_CREATE_MPQ()
            //{
            //    cbSize = unchecked((uint)Marshal.SizeOf(typeof(SFILE_CREATE_MPQ))),
            //    dwMaxFileCount = unchecked((uint)maxFileCount),
            //    dwMpqVersion = (uint)version,
            //    dwFileFlags1 = (uint)listfileAttributes,
            //    dwFileFlags2 = (uint)attributesFileAttributes,
            //    dwStreamFlags = (uint)flags,
            //};

            //if (!NativeMethods.SFileCreateArchive2(filePath, ref create, out _handle))
            //    throw new Win32Exception();
            if (!NativeMethods.SFileCreateArchive(filePath, (uint)flags, int.MaxValue, out _handle))
                throw new Win32Exception();
        }

        public static MpqArchive CreateNew(string mpqPath, MpqArchiveVersion version)
        {
            return CreateNew(mpqPath, version, MpqFileStreamAttributes.None, MpqFileStreamAttributes.None, int.MaxValue);
        }

        public static MpqArchive CreateNew(string mpqPath, MpqArchiveVersion version, MpqFileStreamAttributes listfileAttributes,
            MpqFileStreamAttributes attributesFileAttributes, int maxFileCount)
        {
            return new MpqArchive(mpqPath, version, listfileAttributes, attributesFileAttributes, maxFileCount);
        }
        #endregion

        #region Properties
        // TODO: Move to common location.
        // This is a global setting, not per-archive setting.

        //public int Locale
        //{
        //    get
        //    {
        //        throw new NotImplementedException();
        //    }
        //    set
        //    {
        //        throw new NotImplementedException();
        //    }
        //}

        public long MaxFileCount
        {
            get
            {
                VerifyHandle();
                return NativeMethods.SFileGetMaxFileCount(_handle);
            }
            set
            {
                if (value < 0 || value > uint.MaxValue)
                    throw new ArgumentException("value");
                VerifyHandle();

                if (!NativeMethods.SFileSetMaxFileCount(_handle, unchecked((uint)value)))
                    throw new Win32Exception();
            }
        }

        private void VerifyHandle()
        {
            if (_handle == null || _handle.IsInvalid)
                throw new ObjectDisposedException("MpqArchive");
        }

        public bool IsPatchedArchive
        {
            get
            {
                VerifyHandle();
                return NativeMethods.SFileIsPatchedArchive(_handle);
            }
        }
        #endregion

        public void Flush()
        {
            VerifyHandle();
            if (!NativeMethods.SFileFlushArchive(_handle))
                throw new Win32Exception();
        }

        public int AddListFile(string listfileContents)
        {
            VerifyHandle();
            return NativeMethods.SFileAddListFile(_handle, listfileContents);
        }

        public void AddFileFromDisk(string filePath, string archiveName)
        {
            VerifyHandle();

            if (!NativeMethods.SFileAddFile(_handle, filePath, archiveName, 0))
                throw new Win32Exception();
        }

        public void AddFileFromDiskEx(string filePath, string archiveName, MpqFileAddFileExFlags flags, MpqFileAddFileExCompression compression, MpqFileAddFileExCompression compressionNext)
        {
            VerifyHandle();

            if (!NativeMethods.SFileAddFileEx(_handle, filePath, archiveName, (uint)flags, (uint)compression, (uint)compressionNext))
                throw new Win32Exception();
        }

        public void RemoveFile(string fileName)
        {
            VerifyHandle();

            if (!NativeMethods.SFileRemoveFile(_handle, fileName, 0))
                throw new Win32Exception();
        }

        public void RenameFile(string oldFileName, string newFileName)
        {
            VerifyHandle();

            if (!NativeMethods.SFileRenameFile(_handle, oldFileName, newFileName))
                throw new Win32Exception();
        }

        public void Compact(string listfile)
        {
            VerifyHandle();
            if (!NativeMethods.SFileCompactArchive(_handle, listfile, false))
                throw new Win32Exception();
        }

        private void _OnCompact(IntPtr pvUserData, uint dwWorkType, ulong bytesProcessed, ulong totalBytes)
        {
            MpqArchiveCompactingEventArgs args = new MpqArchiveCompactingEventArgs(dwWorkType, bytesProcessed, totalBytes);
            OnCompacting(args);
        }

        protected virtual void OnCompacting(MpqArchiveCompactingEventArgs e)
        {
            foreach (var cb in _compactCallbacks)
            {
                cb(this, e);
            }
        }

        public event MpqArchiveCompactingEventHandler Compacting
        {
            add
            {
                VerifyHandle();
                _compactCallback = _OnCompact;
                if (!NativeMethods.SFileSetCompactCallback(_handle, _compactCallback, IntPtr.Zero))
                    throw new Win32Exception();

                _compactCallbacks.Add(value);
            }
            remove
            {
                _compactCallbacks.Remove(value);

                VerifyHandle();
                if (_compactCallbacks.Count == 0)
                {
                    if (!NativeMethods.SFileSetCompactCallback(_handle, null, IntPtr.Zero))
                    {
                        // Don't do anything here.  Remove shouldn't fail hard.
                    }
                }
            }
        }

        // TODO: Determine if SFileGetAttributes/SFileSetAttributes/SFileUpdateFileAttributes deserves a projection.
        // It's unclear - these seem to affect the (attributes) file but I can't figure out exactly what that means.

        public void AddPatchArchive(string patchPath)
        {
            VerifyHandle();

            if (!NativeMethods.SFileOpenPatchArchive(_handle, patchPath, null, 0))
                throw new Win32Exception();
        }

        public void AddPatchArchives(IEnumerable<string> patchPaths)
        {
            if (patchPaths == null)
                throw new ArgumentNullException("patchPaths");

            VerifyHandle();

            foreach (string path in patchPaths)
            {
                // Don't sublet to AddPatchArchive to avoid having to repeatedly call VerifyHandle()
                if (!NativeMethods.SFileOpenPatchArchive(_handle, path, null, 0))
                    throw new Win32Exception();
            }
        }

        public bool HasFile(string fileToFind)
        {
            VerifyHandle();

            return NativeMethods.SFileHasFile(_handle, fileToFind);
        }

        public MpqFileStream OpenFile(string fileName)
        {
            VerifyHandle();

            MpqFileSafeHandle fileHandle;
            if (!NativeMethods.SFileOpenFileEx(_handle, fileName, 0, out fileHandle))
                throw new Win32Exception();

            MpqFileStream fs = new MpqFileStream(fileHandle, _accessType, this);
            _openFiles.Add(fs);
            return fs;
        }

        public void ExtractFile(string fileToExtract, string destinationPath)
        {
            VerifyHandle();

            if (!NativeMethods.SFileExtractFile(_handle, fileToExtract, destinationPath, 0))
                throw new Win32Exception();
        }

        public MpqFileVerificationResults VerifyFile(string fileToVerify)
        {
            VerifyHandle();

            return (MpqFileVerificationResults)NativeMethods.SFileVerifyFile(_handle, fileToVerify, 0);
        }

        // TODO: Consider SFileVerifyRawData

        public MpqArchiveVerificationResult VerifyArchive()
        {
            VerifyHandle();

            return (MpqArchiveVerificationResult)NativeMethods.SFileVerifyArchive(_handle);
        }


        #region IDisposable implementation
        public void Dispose()
        {
            Dispose(true);
        }

        ~MpqArchive()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Release owned files first.
                if (_openFiles != null)
                {
                    foreach (var file in _openFiles)
                    {
                        file.Dispose();
                    }

                    _openFiles.Clear();
                    _openFiles = null;
                }

                // Release
                if (_handle != null && !_handle.IsInvalid)
                {
                    _handle.Close();
                    _handle = null;
                }
            }
        }

        internal void RemoveOwnedFile(MpqFileStream file)
        {
            _openFiles.Remove(file);
        }

        #endregion
    }

    public enum MpqArchiveVersion
    {
        Version1 = 0,
        Version2 = 0x01000000,
        Version3 = 0x02000000,
        Version4 = 0x03000000,
    }

    [Flags]
    public enum MpqFileStreamAttributes
    {
        None = 0x0,
    }

    [Flags]
    public enum MpqFileAddFileExFlags : uint
    {
        NONE = 0x0,

        /// <summary>
        /// The file will be compressed using IMPLODE compression method. This flag cannot be used together with MPQ_FILE_COMPRESS. 
        /// If this flag is present, then the dwCompression and dwCompressionNext parameters are ignored. 
        /// 
        /// This flag is obsolete and was used only in Diablo I.
        /// </summary>
        MPQ_FILE_IMPLODE = 0x00000100,

        /// <summary>
        /// The file will be compressed. This flag cannot be used together with MPQ_FILE_IMPLODE.
        /// </summary>
        MPQ_FILE_COMPRESS = 0x00000200,

        /// <summary>
        /// The file will be stored as encrypted.
        /// </summary>
        MPQ_FILE_ENCRYPTED = 0x00010000,

        /// <summary>
        /// The file's encryption key will be adjusted according to file size in the archive. This flag must be used together with MPQ_FILE_ENCRYPTED.
        /// </summary>
        MPQ_FILE_FIX_KEY = 0x00020000,

        /// <summary>
        /// The file will have the deletion marker.
        /// </summary>
        MPQ_FILE_DELETE_MARKER = 0x02000000,

        /// <summary>
        /// The file will have CRC for each file sector. Ignored if the file is not compressed or if the file is stored as single unit.
        /// </summary>
        MPQ_FILE_SECTOR_CRC = 0x04000000,

        /// <summary>
        /// The file will be added as single unit. Files stored as single unit cannot be encrypted, because Blizzard doesn't support them.
        /// </summary>
        MPQ_FILE_SINGLE_UNIT = 0x01000000,

        /// <summary>
        /// If this flag is specified and the file is already in the MPQ, it will be replaced.
        /// </summary>
        MPQ_FILE_REPLACEEXISTING = 0x80000000
    }

    [Flags]
    public enum MpqFileAddFileExCompression : uint
    {
        NONE = 0x0,

        /// <summary>
        /// Use Huffman compression. This bit can only be combined with MPQ_COMPRESSION_ADPCM_MONO or MPQ_COMPRESSION_ADPCM_STEREO.
        /// </summary>
        MPQ_COMPRESSION_HUFFMANN = 0x01,

        /// <summary>
        /// Use ZLIB compression library. This bit cannot be combined with MPQ_COMPRESSION_BZIP2 or MPQ_COMPRESSION_LZMA.
        /// </summary>
        MPQ_COMPRESSION_ZLIB = 0x02,

        /// <summary>
        /// Use Pkware Data Compression Library. This bit cannot be combined with MPQ_COMPRESSION_LZMA.
        /// </summary>
        MPQ_COMPRESSION_PKWARE = 0x08,

        /// <summary>
        /// Use BZIP2 compression library. This bit cannot be combined with MPQ_COMPRESSION_ZLIB or MPQ_COMPRESSION_LZMA.
        /// </summary>
        MPQ_COMPRESSION_BZIP2 = 0x10,

        /// <summary>
        /// Use SPARSE compression. This bit cannot be combined with MPQ_COMPRESSION_LZMA.
        /// </summary>
        MPQ_COMPRESSION_SPARSE = 0x20,

        /// <summary>
        /// Use IMA ADPCM compression for 1-channel (mono) WAVE files. This bit can only be combined with MPQ_COMPRESSION_HUFFMANN. 
        /// This is lossy compression and should only be used for compressing WAVE files.
        /// </summary>
        MPQ_COMPRESSION_ADPCM_MONO = 0x40,

        /// <summary>
        /// Use IMA ADPCM compression for 2-channel (stereo) WAVE files. This bit can only be combined with MPQ_COMPRESSION_HUFFMANN. 
        /// This is lossy compression and should only be used for compressing WAVE files.
        /// </summary>
        MPQ_COMPRESSION_ADPCM_STEREO = 0x80,

        /// <summary>
        /// Use LZMA compression. This value can not be combined with any other compression method.
        /// </summary>
        MPQ_COMPRESSION_LZMA = 0x12,

        /// <summary>
        /// Same compression - only valid for compressionNext
        /// </summary>
        MPQ_COMPRESSION_NEXT_SAME = 0xFF
    }



    [Flags]
    public enum MpqFileVerificationResults
    {
        /// <summary>
        /// There were no errors with the file.
        /// </summary>
        Verified = 0,
        /// <summary>
        /// Failed to open the file
        /// </summary>
        Error = 0x1,
        /// <summary>
        /// Failed to read all data from the file
        /// </summary>
        ReadError = 0x2,
        /// <summary>
        /// File has sector CRC
        /// </summary>
        HasSectorCrc = 0x4,
        /// <summary>
        /// Sector CRC check failed
        /// </summary>
        SectorCrcError = 0x8,
        /// <summary>
        /// File has CRC32
        /// </summary>
        HasChecksum = 0x10,
        /// <summary>
        /// CRC32 check failed
        /// </summary>
        ChecksumError = 0x20,
        /// <summary>
        /// File has data MD5
        /// </summary>
        HasMd5 = 0x40,
        /// <summary>
        /// MD5 check failed
        /// </summary>
        Md5Error = 0x80,
        /// <summary>
        /// File has raw data MD5
        /// </summary>
        HasRawMd5 = 0x100,
        /// <summary>
        /// Raw MD5 check failed
        /// </summary>
        RawMd5Error = 0x200,
    }

    public enum MpqArchiveVerificationResult
    {
        /// <summary>
        /// There is no signature in the MPQ
        /// </summary>
        NoSignature = 0,
        /// <summary>
        /// There was an error during verifying signature (like no memory)
        /// </summary>
        VerificationFailed = 1,
        /// <summary>
        /// There is a weak signature and sign check passed
        /// </summary>
        WeakSignatureVerified = 2,
        /// <summary>
        /// There is a weak signature but sign check failed
        /// </summary>
        WeakSignatureFailed = 3,
        /// <summary>
        /// There is a strong signature and sign check passed
        /// </summary>
        StrongSignatureVerified = 4,
        /// <summary>
        /// There is a strong signature but sign check failed
        /// </summary>
        StrongSignatureFailed = 5,
    }
}
