using System.Collections.Immutable;
using PD2Shared.Logging;
using PD2Shared.Mpq.Internal;
using static PD2Shared.Logging.LoggingStatic;
using PD2Shared.Utils;

namespace PD2Shared.Mpq
{
    public class MpqHierarchy
    {
        private class PathAndSize
        {
            public PathAndSize( string path, long size)
            {
                Path = path;
                Size = size;
            }

            public string Path { get; }
            public long Size { get; }
        }

        public interface IEntry
        {
            public KnownMpqMask MpqsFoundIn { get; }
            public KnownMpq MpqLoadedFrom { get => MpqsFoundIn.GetFinalMpq(); }
            public long Size { get; }
        }

        private class Entry : IEntry
        {
            public KnownMpqMask MpqsFoundIn { get; set; }
            public long Size { get; set; }
        }

        public string MpqDirPath { get; }
        public ImmutableDictionary<string, IEntry> Entries { get; }
        public uint UnknownFileCount { get; }

        private MpqHierarchy(string mpqDirPath, ImmutableDictionary<string, IEntry> entries, uint unknownFileCount)
        {
            MpqDirPath = mpqDirPath;
            Entries = entries;
            UnknownFileCount = unknownFileCount;
        }

        public static async Task<MpqHierarchy> CreateAsync(string mpqDirPath, CancellationToken cancellationToken = default)
        {
            using LoggedRoutine loggedRoutine = new();

            L.CallerInformation($"Building MPQ hierarchy from '{mpqDirPath}'...");

            object totalsLock = new();

            SortedDictionary<KnownMpq, List<PathAndSize>> mpqToPathsAndSizes = new();
            uint unknownFileCountTotal = 0;

            ParallelOptions parallelOptions = new()
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            await Parallel.ForEachAsync(KnownMpqEx.Values, parallelOptions, async (mpq, ct) =>
            {
                var mpqFileName = mpq.ToFileName();
                var mpqPath = Path.Combine(mpqDirPath, mpqFileName);

                IntPtr hMpq = IntPtr.Zero;
                List<PathAndSize> mpqPathsAndSizes = new();
                uint mpqUnknownFileCount = 0;

                L.CallerInformation($"{mpqFileName}: opening '{mpqPath}'...");

                try
                {
                    if (!StormLib.SFileOpenArchive(mpqPath, out hMpq))
                    {
                        if (StormLib.GetLastError() == StormLib.Error.ERROR_FILE_NOT_FOUND)
                        {
                            L.CallerWarning($"{mpqFileName}: not found");
                            return;
                        }
                        else
                        {
                            throw new StormLibException(nameof(StormLib.SFileOpenArchive), mpqPath);
                        }
                    }

                    bool listFileAdded = false;

                    if (!StormLib.SFileHasFile(hMpq, StormLib.MetadataListFilePath))
                    {
                        if (StormLib.GetLastError() == StormLib.Error.ERROR_FILE_NOT_FOUND)
                        {
                            L.CallerWarning($"{mpqFileName}: missing '{StormLib.MetadataListFilePath}'. Loading built-in...");

                            StormLib.Error err = StormLib.SFileAddListFileEntries(hMpq);

                            if (err != StormLib.Error.ERROR_SUCCESS)
                            {
                                throw new StormLibException(err, nameof(StormLib.SFileAddListFileEntries), null!);
                            }

                            listFileAdded = true;
                        }
                        else
                        {
                            throw new StormLibException(nameof(StormLib.SFileHasFile), mpqPath, StormLib.MetadataListFilePath);
                        }
                    }

                    if (!listFileAdded)
                    {
                        // Check if there are any files not covered by the archive's supplied listfile
                        // and if so -- load the built-in listfile.

                        StormLib.SFILE_FIND_DATA findData = new();
                        IntPtr hFind = StormLib.SFileFindFirstFile(hMpq, StormLib.PseudoFileMask, findData);

                        if (hFind == IntPtr.Zero)
                        {
                            if (StormLib.GetLastError() != StormLib.Error.ERROR_NO_MORE_FILES)
                            {
                                throw new StormLibException(nameof(StormLib.SFileFindFirstFile), mpqPath);
                            }
                        }
                        else
                        {
                            do
                            {
                                if (StormLib.IsPseudoFilePath(findData.cFileName))
                                {
                                    L.CallerWarning($"{mpqFileName}: encountered a file outside '{StormLib.MetadataListFilePath}'. Loading built-in...");

                                    StormLib.Error err = StormLib.SFileAddListFileEntries(hMpq);

                                    if (err != StormLib.Error.ERROR_SUCCESS)
                                    {
                                        throw new StormLibException(err, nameof(StormLib.SFileAddListFileEntries), null!);
                                    }

                                    listFileAdded = true;

                                    break;
                                }
                            }
                            while (StormLib.SFileFindNextFile(hFind, findData));

                            if (!listFileAdded)
                            {
                                if (StormLib.GetLastError() != StormLib.Error.ERROR_SUCCESS && StormLib.GetLastError() != StormLib.Error.ERROR_NO_MORE_FILES)
                                {
                                    throw new StormLibException(nameof(StormLib.SFileFindNextFile), mpqPath);
                                }
                            }

                            if (!StormLib.SFileFindClose(hFind))
                            {
                                throw new StormLibException(nameof(StormLib.SFileFindClose), mpqPath);
                            }
                        }
                    }

                    if (!StormLib.SFileGetNumberOfFiles(hMpq, out uint numberOfFiles))
                    {
                        throw new StormLibException(nameof(StormLib.SFileGetNumberOfFiles), mpqPath);
                    }

                    mpqPathsAndSizes.EnsureCapacity((int)numberOfFiles);

                    // Enumerate all the files
                    {
                        StormLib.SFILE_FIND_DATA findData = new();
                        IntPtr hFind = StormLib.SFileFindFirstFile(hMpq, "*", findData);

                        if (hFind == IntPtr.Zero)
                        {
                            if (StormLib.GetLastError() != StormLib.Error.ERROR_NO_MORE_FILES)
                            {
                                throw new StormLibException(nameof(StormLib.SFileFindFirstFile), mpqPath);
                            }

                            L.CallerWarning($"{mpqFileName}: no files found");
                        }
                        else
                        {
                            do
                            {
                                // Ignore metadata
                                if (StormLib.IsMetadataFilePath(findData.cFileName))
                                {
                                    continue;
                                }

                                if (StormLib.IsPseudoFilePath(findData.cFileName))
                                {
                                    ++mpqUnknownFileCount;
                                    continue;
                                }

                                mpqPathsAndSizes.Add(new PathAndSize(findData.cFileName, findData.dwFileSize));
                            }
                            while (StormLib.SFileFindNextFile(hFind, findData));

                            if (StormLib.GetLastError() != StormLib.Error.ERROR_SUCCESS && StormLib.GetLastError() != StormLib.Error.ERROR_NO_MORE_FILES)
                            {
                                throw new StormLibException(nameof(StormLib.SFileFindNextFile), mpqPath);
                            }

                            if (!StormLib.SFileFindClose(hFind))
                            {
                                throw new StormLibException(nameof(StormLib.SFileFindClose), mpqPath);
                            }
                        }
                    }
                }
                finally
                {
                    if (hMpq != IntPtr.Zero)
                    {
                        if (!StormLib.SFileCloseArchive(hMpq))
                        {
                            L.CallerError(new StormLibException(nameof(StormLib.SFileCloseArchive), mpqPath), string.Empty);
                        }

                        hMpq = IntPtr.Zero;
                    }
                }

                var unknownFilesStr = mpqUnknownFileCount == 0 ? string.Empty : $" (ignoring {mpqUnknownFileCount:N0} unknown)";

                L.CallerWrite(mpqUnknownFileCount == 0 ? Serilog.Events.LogEventLevel.Information : Serilog.Events.LogEventLevel.Warning,
                    $"{mpqFileName}: found {mpqPathsAndSizes.Count:N0} file(s){unknownFilesStr} ({Formatting.FormatSizeInMiB(mpqPathsAndSizes.Sum(pathAndSize => pathAndSize.Size))} total)");

                lock (totalsLock)
                {
                    mpqToPathsAndSizes.Add(mpq, mpqPathsAndSizes);
                    unknownFileCountTotal += mpqUnknownFileCount;
                }
            }).ConfigureAwait(false);

            // Combine all Task results

            var totalFileCount = mpqToPathsAndSizes.Sum(kvp => kvp.Value.Count);

            Dictionary<string, Entry> entries = new(totalFileCount, StringComparer.OrdinalIgnoreCase);

            if (totalFileCount > 0)
            {
                L.CallerInformation($"Combining MPQ files into a hierarchy...");
                L.CallerInformation($"({string.Join(" << ", mpqToPathsAndSizes.Keys.Select(mpq => mpq.ToFileName()))})");

                foreach (var kvp in mpqToPathsAndSizes)
                {
                    var mpq = kvp.Key;
                    var pathsAndSizes = kvp.Value;

                    foreach (var pathAndSize in pathsAndSizes)
                    {
                        if (!entries.TryGetValue(pathAndSize.Path, out Entry? entry))
                        {
                            entry = new Entry();
                            entries.Add(pathAndSize.Path, entry);
                        }

                        entry.Size = pathAndSize.Size;
                        entry.MpqsFoundIn |= (KnownMpqMask)mpq;
                    }
                }
            }

            // ...and initialize an instance

            MpqHierarchy mpqHierarchy = new(
                mpqDirPath,
                entries.ToImmutableDictionary(kvp => kvp.Key, kvp => (IEntry)kvp.Value, StringComparer.OrdinalIgnoreCase),
                unknownFileCountTotal);

            var unknownFilesStr = mpqHierarchy.UnknownFileCount == 0 ? string.Empty : $" (ignoring {mpqHierarchy.UnknownFileCount:N0} unknown)";

            L.CallerWrite(mpqHierarchy.UnknownFileCount == 0 ? Serilog.Events.LogEventLevel.Information : Serilog.Events.LogEventLevel.Warning,
                $"Found {totalFileCount:N0} -> {mpqHierarchy.Entries.Count:N0} unique file(s){unknownFilesStr} ({Formatting.FormatSizeInMiB(mpqHierarchy.Entries.Sum(kvp => kvp.Value.Size))} total)");

            return mpqHierarchy;
        }

        public static MpqHierarchy Create(string mpqDirPath)
        {
            return Task.Factory.StartNew(() => CreateAsync(mpqDirPath)).Unwrap().GetAwaiter().GetResult();
        }
    }
}
