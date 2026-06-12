using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using PD2Shared.Logging;
using static PD2Shared.Logging.LoggingStatic;
using PD2Shared.Utils;

namespace PD2Shared.Mpq.Internal
{
    internal static class StormLib
    {
        private static class Kernel32
        {
            [DllImport("kernel32.dll")]
            public static extern void RtlZeroMemory(IntPtr dst, UIntPtr length);
        }

        public const string DllFileName = "StormLib.dll";

        public enum Error : int
        {
#pragma warning disable format
            // Generic errors (taken from StormPort.h and assigned values from winerror.h)

            ERROR_SUCCESS                = 0,
            ERROR_FILE_NOT_FOUND         = 2,
            ERROR_ACCESS_DENIED          = 5,
            ERROR_INVALID_HANDLE         = 6,
            ERROR_NOT_ENOUGH_MEMORY      = 8,
            ERROR_NOT_SUPPORTED          = 50,
            ERROR_INVALID_PARAMETER      = 87,
            ERROR_NEGATIVE_SEEK          = 131,
            ERROR_DISK_FULL              = 112,
            ERROR_ALREADY_EXISTS         = 183,
            ERROR_INSUFFICIENT_BUFFER    = 122,
            ERROR_BAD_FORMAT             = 11,
            ERROR_NO_MORE_FILES          = 18,
            ERROR_HANDLE_EOF             = 38,
            ERROR_CAN_NOT_COMPLETE       = 1003,
            ERROR_FILE_CORRUPT           = 1392,
            ERROR_BUFFER_OVERFLOW        = 111,
            ERROR_INVALID_DATA           = 13,
            ERROR_NO_UNICODE_TRANSLATION = 1113,

            // StormLib-specific errors

            ERROR_AVI_FILE               = 10000,  // Not a MPQ file, but an AVI file.
            ERROR_UNKNOWN_FILE_KEY       = 10001,  // Returned by SFileReadFile when can't find file key
            ERROR_CHECKSUM_ERROR         = 10002,  // Returned by SFileReadFile when sector CRC doesn't match
            ERROR_INTERNAL_FILE          = 10003,  // The given operation is not allowed on internal file
            ERROR_BASE_FILE_MISSING      = 10004,  // The file is present as incremental patch file, but base file is missing
            ERROR_MARKED_FOR_DELETE      = 10005,  // The file was marked as "deleted" in the MPQ
            ERROR_FILE_INCOMPLETE        = 10006,  // The required file part is missing
            ERROR_UNKNOWN_FILE_NAMES     = 10007,  // A name of at least one file is unknown
            ERROR_CANT_FIND_PATCH_PREFIX = 10008,  // StormLib was unable to find patch prefix for the patches
            ERROR_FAKE_MPQ_HEADER        = 10009,  // The header at this position is fake header
            ERROR_FILE_DELETED           = 10010,  // The file is present but contains delete marker
#pragma warning restore format
        }

        private const Error FirstStormLibSpecificError = Error.ERROR_AVI_FILE;
        private const Error LastStormLibSpecificError = Error.ERROR_FILE_DELETED;

        public static string GetLastErrorMessage(Error error)
        {
            if (error >= FirstStormLibSpecificError && error <= LastStormLibSpecificError)
            {
                return Enum.GetName<Error>(error)!;
            }

            return $"'{Win32.GetLastErrorMessage((int)error)}' ({Enum.GetName<Error>(error) ?? ((int)error).ToString()})";
        }

        public static string GetLastErrorMessage()
        {
            return GetLastErrorMessage((Error)Marshal.GetLastPInvokeError());
        }

        public static Error GetLastError()
        {
            return (Error)Marshal.GetLastPInvokeError();
        }

        [Flags]
        private enum SFileOpenArchiveFlags : uint
        {
#pragma warning disable format
            BASE_PROVIDER_FILE        = 0x00000000,  // Base data source is a file
            BASE_PROVIDER_MAP         = 0x00000001,  // Base data source is memory-mapped file
            BASE_PROVIDER_HTTP        = 0x00000002,  // Base data source is a file on web server
            BASE_PROVIDER_MASK        = 0x0000000F,  // Mask for base provider value

            STREAM_PROVIDER_FLAT      = 0x00000000,  // Stream is linear with no offset mapping
            STREAM_PROVIDER_PARTIAL   = 0x00000010,  // Stream is partial file (.part)
            STREAM_PROVIDER_MPQE      = 0x00000020,  // Stream is an encrypted MPQ
            STREAM_PROVIDER_BLOCK4    = 0x00000030,  // 0x4000 per block, text MD5 after each block, max 0x2000 blocks per file
            STREAM_PROVIDER_MASK      = 0x000000F0,  // Mask for stream provider value

            STREAM_FLAG_READ_ONLY     = 0x00000100,  // Stream is read only
            STREAM_FLAG_WRITE_SHARE   = 0x00000200,  // Allow write sharing when open for write
            STREAM_FLAG_USE_BITMAP    = 0x00000400,  // If the file has a file bitmap, load it and use it
            STREAM_OPTIONS_MASK       = 0x0000FF00,  // Mask for stream options

            STREAM_PROVIDERS_MASK     = 0x000000FF,  // Mask to get stream providers
            STREAM_FLAGS_MASK         = 0x0000FFFF,  // Mask for all stream flags (providers+options)

            MPQ_OPEN_NO_LISTFILE      = 0x00010000,  // Don't load the internal listfile
            MPQ_OPEN_NO_ATTRIBUTES    = 0x00020000,  // Don't open the attributes
            MPQ_OPEN_NO_HEADER_SEARCH = 0x00040000,  // Don't search for the MPQ header past the begin of the file
            MPQ_OPEN_FORCE_MPQ_V1     = 0x00080000,  // Always open the archive as MPQ v 1.00, ignore the "wFormatVersion" variable in the header
            MPQ_OPEN_CHECK_SECTOR_CRC = 0x00100000,  // On files with MPQ_FILE_SECTOR_CRC, the CRC will be checked when reading file
            MPQ_OPEN_PATCH            = 0x00200000,  // This archive is a patch MPQ. Used internally.
            MPQ_OPEN_FORCE_LISTFILE   = 0x00400000,  // Force add listfile even if there is none at the moment of opening
#pragma warning restore format
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SFileOpenArchive([MarshalAs(UnmanagedType.LPWStr)] string szMpqName, uint dwPriority, SFileOpenArchiveFlags dwFlags, out IntPtr phMpq);

        private static bool SFileOpenArchive(string szMpqName, bool loadListFile, out IntPtr phMpq)
        {
            var flags =
                SFileOpenArchiveFlags.STREAM_PROVIDER_FLAT |
                SFileOpenArchiveFlags.BASE_PROVIDER_FILE |
                SFileOpenArchiveFlags.STREAM_FLAG_READ_ONLY |
                // Don't load (attributes) as we'll be providing hashes
                SFileOpenArchiveFlags.MPQ_OPEN_NO_ATTRIBUTES;

            if (!loadListFile)
            {
                flags |= SFileOpenArchiveFlags.MPQ_OPEN_NO_LISTFILE;
            }

            return SFileOpenArchive(szMpqName, dwPriority: 0, flags, out phMpq);
        }

        public static bool SFileOpenArchive(string szMpqName, out IntPtr phMpq)
        {
            return SFileOpenArchive(szMpqName, loadListFile: true, out phMpq);
        }

        public static bool SFileOpenArchiveNoListFile(string szMpqName, out IntPtr phMpq)
        {
            return SFileOpenArchive(szMpqName, loadListFile: false, out phMpq);
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName)]
        [return: MarshalAs(UnmanagedType.U4)]
        private static extern Error SFileAddListFileEntries(IntPtr hMpq, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr, SizeParamIndex = 2)][In] string[] listFileEntries, uint dwEntryCount);

        public static Error SFileAddListFileEntries(IntPtr hMpq)
        {
            return SFileAddListFileEntries(hMpq, ListFileStatic.ListFile, (uint)ListFileStatic.ListFile.Length);
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SFileCloseArchive(IntPtr hMpq);

        private enum SFileInfoClass
        {
#pragma warning disable format
            // Info classes for archives
            SFileMpqFileName,                       // Name of the archive file (TCHAR [])
            SFileMpqStreamBitmap,                   // Array of bits, each bit means availability of one block (BYTE [])
            SFileMpqUserDataOffset,                 // Offset of the user data header (ULONGLONG)
            SFileMpqUserDataHeader,                 // Raw (unfixed) user data header (TMPQUserData)
            SFileMpqUserData,                       // MPQ User data, without the header (BYTE [])
            SFileMpqHeaderOffset,                   // Offset of the MPQ header (ULONGLONG)
            SFileMpqHeaderSize,                     // Fixed size of the MPQ header
            SFileMpqHeader,                         // Raw (unfixed) archive header (TMPQHeader)
            SFileMpqHetTableOffset,                 // Offset of the HET table, relative to MPQ header (ULONGLONG)
            SFileMpqHetTableSize,                   // Compressed size of the HET table (ULONGLONG)
            SFileMpqHetHeader,                      // HET table header (TMPQHetHeader)
            SFileMpqHetTable,                       // HET table as pointer. Must be freed using SFileFreeFileInfo
            SFileMpqBetTableOffset,                 // Offset of the BET table, relative to MPQ header (ULONGLONG)
            SFileMpqBetTableSize,                   // Compressed size of the BET table (ULONGLONG)
            SFileMpqBetHeader,                      // BET table header, followed by the flags (TMPQBetHeader + DWORD[])
            SFileMpqBetTable,                       // BET table as pointer. Must be freed using SFileFreeFileInfo
            SFileMpqHashTableOffset,                // Hash table offset, relative to MPQ header (ULONGLONG)
            SFileMpqHashTableSize64,                // Compressed size of the hash table (ULONGLONG)
            SFileMpqHashTableSize,                  // Size of the hash table, in entries (DWORD)
            SFileMpqHashTable,                      // Raw (unfixed) hash table (TMPQBlock [])
            SFileMpqBlockTableOffset,               // Block table offset, relative to MPQ header (ULONGLONG)
            SFileMpqBlockTableSize64,               // Compressed size of the block table (ULONGLONG)
            SFileMpqBlockTableSize,                 // Size of the block table, in entries (DWORD)
            SFileMpqBlockTable,                     // Raw (unfixed) block table (TMPQBlock [])
            SFileMpqHiBlockTableOffset,             // Hi-block table offset, relative to MPQ header (ULONGLONG)
            SFileMpqHiBlockTableSize64,             // Compressed size of the hi-block table (ULONGLONG)
            SFileMpqHiBlockTable,                   // The hi-block table (USHORT [])
            SFileMpqSignatures,                     // Signatures present in the MPQ (DWORD)
            SFileMpqStrongSignatureOffset,          // Byte offset of the strong signature, relative to begin of the file (ULONGLONG)
            SFileMpqStrongSignatureSize,            // Size of the strong signature (DWORD)
            SFileMpqStrongSignature,                // The strong signature (BYTE [])
            SFileMpqArchiveSize64,                  // Archive size from the header (ULONGLONG)
            SFileMpqArchiveSize,                    // Archive size from the header (DWORD)
            SFileMpqMaxFileCount,                   // Max number of files in the archive (DWORD)
            SFileMpqFileTableSize,                  // Number of entries in the file table (DWORD)
            SFileMpqSectorSize,                     // Sector size (DWORD)
            SFileMpqNumberOfFiles,                  // Number of files (DWORD)
            SFileMpqRawChunkSize,                   // Size of the raw data chunk for MD5
            SFileMpqStreamFlags,                    // Stream flags (DWORD)
            SFileMpqFlags,                          // Nonzero if the MPQ is read only (DWORD)

            // Info classes for files
            SFileInfoPatchChain,                    // Chain of patches where the file is (TCHAR [])
            SFileInfoFileEntry,                     // The file entry for the file (TFileEntry)
            SFileInfoHashEntry,                     // Hash table entry for the file (TMPQHash)
            SFileInfoHashIndex,                     // Index of the hash table entry (DWORD)
            SFileInfoNameHash1,                     // The first name hash in the hash table (DWORD)
            SFileInfoNameHash2,                     // The second name hash in the hash table (DWORD)
            SFileInfoNameHash3,                     // 64-bit file name hash for the HET/BET tables (ULONGLONG)
            SFileInfoLocale,                        // File locale (DWORD)
            SFileInfoFileIndex,                     // Block index (DWORD)
            SFileInfoByteOffset,                    // File position in the archive (ULONGLONG)
            SFileInfoFileTime,                      // File time (ULONGLONG)
            SFileInfoFileSize,                      // Size of the file (DWORD)
            SFileInfoCompressedSize,                // Compressed file size (DWORD)
            SFileInfoFlags,                         // File flags from (DWORD)
            SFileInfoEncryptionKey,                 // File encryption key
            SFileInfoEncryptionKeyRaw,              // Unfixed value of the file key
            SFileInfoCRC32,                         // CRC32 of the file

            SFileInfoInvalid = 0xFFF,               // Invalid file info class
#pragma warning restore format
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SFileGetFileInfo(IntPtr hMpqOrFile, SFileInfoClass InfoClass, out UInt32 pvFileInfo, uint cbFileInfo, out uint pcbLengthNeeded);

        private static bool SFileGetFileInfo(IntPtr hMpqOrFile, SFileInfoClass InfoClass, out UInt32 pvFileInfo)
        {
            return SFileGetFileInfo(hMpqOrFile, InfoClass, out pvFileInfo, (uint)Marshal.SizeOf(typeof(UInt32)), out _);
        }

        public static bool SFileGetNumberOfFiles(IntPtr hMpq, out UInt32 numberOfFiles)
        {
            return SFileGetFileInfo(hMpq, SFileInfoClass.SFileMpqNumberOfFiles, out numberOfFiles);
        }

        // Structure for SFileFindFirstFile and SFileFindNextFile
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class SFILE_FIND_DATA
        {
            public SFILE_FIND_DATA()
            {
                cFileName = string.Empty;
                szPlainName = string.Empty;
            }

#pragma warning disable format
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Win32.MAX_PATH)]
            public string cFileName;                         // Full name of the found file
            [MarshalAs(UnmanagedType.LPStr)]
            public string szPlainName;                       // Plain name of the found file
            public uint dwHashIndex;                         // Hash table index for the file (HAH_ENTRY_FREE if no hash table)
            public uint dwBlockIndex;                        // Block table index for the file
            public uint dwFileSize;                          // File size in bytes
            public uint dwFileFlags;                         // MPQ file flags
            public uint dwCompSize;                          // Compressed file size
            public uint dwFileTimeLo;                        // Low 32-bits of the file time (0 if not present)
            public uint dwFileTimeHi;                        // High 32-bits of the file time (0 if not present)
            public uint lcLocale;                            // Compound of file locale (16 bits) and platform (8 bits)
#pragma warning restore format
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        // <!> Can't get this to work directly
        // public static extern IntPtr SFileFindFirstFile(IntPtr hMpqOrFile, [MarshalAs(UnmanagedType.LPStr)] string szMask, [Out] SFILE_FIND_DATA lpFindFileData, [MarshalAs(UnmanagedType.LPWStr)] string? szListFile = null);
        private static extern IntPtr SFileFindFirstFile(IntPtr hMpqOrFile, [MarshalAs(UnmanagedType.LPStr)] string szMask, IntPtr lpFindFileData, [MarshalAs(UnmanagedType.LPWStr)] string? szListFile = null);

        public static IntPtr SFileFindFirstFile(IntPtr hMpqOrFile, string szMask, SFILE_FIND_DATA lpFindFileData, string? szListFile = null)
        {
            IntPtr structPtr = IntPtr.Zero;

            try
            {
                int structSize = Marshal.SizeOf(lpFindFileData);
                structPtr = Marshal.AllocHGlobal(structSize);
                // Zero this memory instead of copying the managed object (as its effectively an [Out] anyway).
                // This is to make sure that Marshal.PtrToStructure() won't read back garbage and trip up.
                Kernel32.RtlZeroMemory(structPtr, (UIntPtr)structSize);

                IntPtr hFind = StormLib.SFileFindFirstFile(hMpqOrFile, szMask, structPtr, szListFile);

                Marshal.PtrToStructure(structPtr, lpFindFileData);

                return hFind;
            }
            finally
            {
                Marshal.FreeHGlobal(structPtr);
            }
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        // <!> Can't get this one to work either
        // public static extern bool SFileFindNextFile(IntPtr hFind, [Out] SFILE_FIND_DATA lpFindFileData);
        private static extern bool SFileFindNextFile(IntPtr hFind, IntPtr lpFindFileData);

        public static bool SFileFindNextFile(IntPtr hFind, SFILE_FIND_DATA lpFindFileData)
        {
            IntPtr structPtr = IntPtr.Zero;

            try
            {
                int structSize = Marshal.SizeOf(lpFindFileData);
                structPtr = Marshal.AllocHGlobal(structSize);
                // Zero this memory instead of copying the managed object (as its effectively an [Out] anyway).
                // This is to make sure that Marshal.PtrToStructure() won't read back garbage and trip up.
                Kernel32.RtlZeroMemory(structPtr, (UIntPtr)structSize);

                bool res = StormLib.SFileFindNextFile(hFind, structPtr);

                Marshal.PtrToStructure(structPtr, lpFindFileData);

                return res;
            }
            finally
            {
                Marshal.FreeHGlobal(structPtr);
            }
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SFileFindClose(IntPtr hFind);

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SFileHasFile(IntPtr hMpq, [MarshalAs(UnmanagedType.LPStr)] string szFileName);

        [Flags]
        private enum SFileOpenFileExFlags : uint
        {
#pragma warning disable format
            SFILE_OPEN_FROM_MPQ   = 0x00000000,  // Open the file from the MPQ archive
            SFILE_OPEN_LOCAL_FILE = 0xFFFFFFFF,  // Open a local file
#pragma warning restore format
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SFileOpenFileEx(IntPtr hMpq, [MarshalAs(UnmanagedType.LPStr)] string szFileName, SFileOpenFileExFlags dwSearchScope, out IntPtr hFile);

        public static bool SFileOpenFileEx(IntPtr hMpq, string szFileName, out IntPtr hFile)
        {
            return SFileOpenFileEx(hMpq, szFileName, SFileOpenFileExFlags.SFILE_OPEN_FROM_MPQ, out hFile);
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SFileReadFile(IntPtr hFile, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)][Out] byte[] lpBuffer, uint dwToRead, out uint dwRead, IntPtr lpOverlapped);

        public static bool SFileReadFile(IntPtr hFile, byte[] buffer, out uint bytesRead)
        {
            return SFileReadFile(hFile, buffer, (uint)buffer.Length, out bytesRead, lpOverlapped: IntPtr.Zero);
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SFileCloseFile(IntPtr hFile);

        // Rename GetLastError() to TestCall() as its only purpose is to force-load the DLL.
        // Since StormLib's GetLastError actually redirects to Kernel32.GetLastError there's no point in using it.
        // Instead, prefer custom GetLastError() which utilizes Marshal.GetLastPInvokeError() underneath.
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport(DllFileName, EntryPoint = "GetLastError")]
        [return: MarshalAs(UnmanagedType.U4)]
        public static extern uint TestCall();

        private static readonly Regex PseudoFileNameRegex = new(@"File\d{8}\..+", RegexOptions.Compiled);
        public static bool IsPseudoFilePath(string filePath)
        {
            return PseudoFileNameRegex.IsMatch(filePath);
        }

        public const string PseudoFileMask = "File????????.*";

        public const string MetadataListFilePath = "(listfile)";

        public static bool IsMetadataFilePath(string filePath)
        {
            return filePath.StartsWith('(') && filePath.EndsWith(')');
        }

        public static Version? Version { get; } = null;
        public static string? VersionString { get; } = null;

        static StormLib()
        {
            // Force-load the DLL in order to examine its FileVersionInfo as StormLib provides no dedicated
            // routines for querying its version number etc...
            _ = StormLib.TestCall();

            IntPtr hModule = Utils.PInvoke.Kernel32.GetModuleHandle(StormLib.DllFileName);

            if (hModule == IntPtr.Zero)
            {
                L.CallerWarning($"{nameof(Utils.PInvoke.Kernel32.GetModuleHandle)}() failed for '{StormLib.DllFileName}' with '{Win32.GetLastErrorMessage()}'.");
                return;
            }

            string? moduleFileName = Utils.PInvoke.Kernel32.GetModuleFileName(hModule);

            if (moduleFileName == null)
            {
                L.CallerWarning($"{nameof(Utils.PInvoke.Kernel32.GetModuleFileName)}() failed with '{Win32.GetLastErrorMessage()}'.");
                return;
            }

            try
            {
                var vi = FileVersionInfo.GetVersionInfo(moduleFileName);

                if (vi.ProductVersion != null)
                {
                    Version = new Version(vi.ProductMajorPart, vi.ProductMinorPart);
                    VersionString = $"{vi.ProductMajorPart}.{vi.ProductMinorPart}{(vi.IsPrivateBuild ? $" ({vi.PrivateBuild})" : string.Empty)}";
                }
            }
            catch (FileNotFoundException ex)
            {
                L.CallerWarning(ex, $"{nameof(FileVersionInfo.GetVersionInfo)}() failed.");
            }
        }
    }
}
