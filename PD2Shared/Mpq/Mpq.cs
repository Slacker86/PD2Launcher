using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using PD2Shared.Extensions;
using PD2Shared.GameFileUpdate;
using PD2Shared.GameFileUpdate.Internal;
using PD2Shared.Logging;
using static PD2Shared.Logging.LoggingStatic;
using PD2Shared.Mpq.Internal;
using PD2Shared.Utils;

namespace PD2Shared.Mpq
{
    public static class Mpq
    {
        // Expected sector size of encountered MPQs should be 4096 bytes
        private const int MpqReadBufferSize = 4096 * 10;

        public static Version? StormLibVersion { get => StormLib.Version; }
        public static string? StormLibVersionString { get => StormLib.VersionString; }

        public static LanguageVersion DetectBaseLanguageVersion(MpqHierarchy hierarchy)
        {
            const string LanguageFilePath = @"data\local\use";

            IntPtr hMpq = IntPtr.Zero;
            IntPtr hFile = IntPtr.Zero;

            MpqHierarchy.IEntry? entry = null;

            try
            {
                if (!hierarchy.Entries.TryGetValue(LanguageFilePath, out entry))
                {
                    L.CallerWarning($"Language file ('{LanguageFilePath}') not found.");
                    return LanguageVersion.Unknown;
                }

                if (entry.MpqLoadedFrom != KnownMpq.D2data)
                {
                    L.CallerWarning($"Language file ('{LanguageFilePath}') found in {entry.MpqLoadedFrom.ToFileName()} and not {KnownMpq.D2data.ToFileName()}.");
                }

                Debug.Assert(entry != null);

                var mpqPath = Path.Combine(Env.GetBaseGameDirPath(), entry.MpqLoadedFrom.ToFileName());

                if (!StormLib.SFileOpenArchiveNoListFile(mpqPath, out hMpq))
                {
                    throw new StormLibException(nameof(StormLib.SFileOpenArchiveNoListFile), mpqPath);
                }

                if (!StormLib.SFileOpenFileEx(hMpq, LanguageFilePath, out hFile))
                {
                    throw new StormLibException(nameof(StormLib.SFileOpenFileEx), entry.MpqLoadedFrom.ToFileName(), LanguageFilePath);
                }

                byte[] buffer = new byte[1];

                if (!StormLib.SFileReadFile(hFile, buffer, out _))
                {
                    throw new StormLibException(nameof(StormLib.SFileReadFile), entry.MpqLoadedFrom.ToFileName(), LanguageFilePath);
                }

                // Taken from https://d2mods.info/forum/viewtopic.php?p=78007#p78007
                return buffer[0] switch
                {
                    0x00 => LanguageVersion.English,
                    0x01 => LanguageVersion.Spanish,
                    0x02 => LanguageVersion.German,
                    0x03 => LanguageVersion.French,
                    0x04 => LanguageVersion.Portuguese,
                    0x05 => LanguageVersion.Italian,
                    0x06 => LanguageVersion.Japanese,
                    0x07 => LanguageVersion.Korean,

                    0x08 => LanguageVersion.Unknown,

                    0x09 => LanguageVersion.Chinese,
                    0x0A => LanguageVersion.Polish,
                    0x0B => LanguageVersion.Russian,

                    0x0C => LanguageVersion.English,

                    _ => LanguageVersion.Unknown
                };
            }
            finally
            {
                if (hFile != IntPtr.Zero)
                {
                    if (!StormLib.SFileCloseFile(hFile))
                    {
                        L.CallerError(new StormLibException(nameof(StormLib.SFileCloseFile), entry?.MpqLoadedFrom.ToFileName()!, LanguageFilePath), string.Empty);
                    }
                }

                if (hMpq != IntPtr.Zero)
                {
                    if (!StormLib.SFileCloseArchive(hMpq))
                    {
                        L.CallerError(new StormLibException(nameof(StormLib.SFileCloseArchive), entry?.MpqLoadedFrom.ToFileName()!), string.Empty);
                    }
                }
            }
        }

        public static LanguageVersion DetectExpansionLanguageVersion(MpqHierarchy hierarchy)
        {
            string GetPath(LanguageVersion languageVersion)
            {
                return @$"data\local\UI\{languageVersion.To3LetterAbbrev()}\Expansion\A5L1.dc6";
            }

            // Exclude English since other versions might as well contain some English files
            foreach (var lv in LanguageVersionEx.NonEnglishLanguageVersions)
            {
                if (hierarchy.Entries.ContainsKey(GetPath(lv)))
                {
                    return lv;
                }
            }

            if (hierarchy.Entries.ContainsKey(GetPath(LanguageVersion.English)))
            {
                return LanguageVersion.English;
            }

            return LanguageVersion.Unknown;
        }

        public static List<VerificationFailure> QuickVerifyMpqs(MpqHierarchy hierarchy)
        {
            L.CallerInformation($"Performing quick verification of MPQ hierarchy of {hierarchy.Entries.Count:N0} files against {MpqManifestStatic.Entries.Length:N0} MPQ manifest entries...");

            List<VerificationFailure> failureList = new(hierarchy.Entries.Count);

            const int FailuresToLogMax = 30;
            int errorsLogged = 0;

            // Verify file presence, sizes and containing MPQ only

            foreach (var manifestEntry in MpqManifestStatic.Entries)
            {
                if (!hierarchy.Entries.TryGetValue(manifestEntry.Path, out MpqHierarchy.IEntry? hierarchyEntry))
                {
                    if (errorsLogged < FailuresToLogMax)
                    {
                        L.CallerError($"{manifestEntry.ExpectedMpq.ToFileName()}: {manifestEntry.Path}: not found");
                        ++errorsLogged;
                    }

                    failureList.Add(new VerificationFailure(VerificationFailure.FailureKind.FileNotFound, manifestEntry, hierarchyEntry: null));
                    continue;
                }

                if (manifestEntry.Size != null && manifestEntry.Size != hierarchyEntry.Size)
                {
                    if (errorsLogged < FailuresToLogMax)
                    {
                        L.CallerError($"{hierarchyEntry.MpqLoadedFrom.ToFileName()}: {manifestEntry.Path}: has incorrect size: {manifestEntry.Size} != {hierarchyEntry.Size} (actual)");
                        ++errorsLogged;
                    }

                    failureList.Add(new VerificationFailure(VerificationFailure.FailureKind.SizeMismatch, manifestEntry, hierarchyEntry));
                }

                if (manifestEntry.ExpectedMpq != hierarchyEntry.MpqLoadedFrom)
                {
                    if (errorsLogged < FailuresToLogMax)
                    {
                        L.CallerWarning($"{hierarchyEntry.MpqLoadedFrom.ToFileName()}: {manifestEntry.Path}: found in an unexpected MPQ: {manifestEntry.ExpectedMpq.ToFileName()} != {hierarchyEntry.MpqLoadedFrom.ToFileName()} (actual)");
                        ++errorsLogged;
                    }

                    failureList.Add(new VerificationFailure(VerificationFailure.FailureKind.MpqMismatch, manifestEntry, hierarchyEntry));
                }
            }

            if (failureList.Count > FailuresToLogMax)
            {
                L.CallerWarning($"...and {failureList.Count - FailuresToLogMax} more failures.");
            }

            return failureList;
        }

        public static async Task<List<VerificationFailure>> VerifyMpqsAsync(
            MpqHierarchy hierarchy,
            IProgress<ProgressValues.IData>? progress = null,
            IProgress<bool>? errorIndicatorProgress = null,
            VerificationExclusions exclusions = VerificationExclusions.AudioVideo,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(hierarchy, nameof(hierarchy));

            using LoggedRoutine loggedRoutine = new();

            // Perform a preliminary quick verification first

            var failureList = QuickVerifyMpqs(hierarchy);

            if (failureList.Any(f => f.IsError))
            {
                L.CallerDebug($"{nameof(QuickVerifyMpqs)}() failed. Bailing early.");

                return failureList;
            }

            // Filter files eligible for hash verification based on VerificationExclusions

            L.CallerInformation($"Assets to exclude from hash verification: {exclusions.ToPrettyString()}");

            bool filterPredicate(MpqManifestEntry manifestEntry, MpqHierarchy.IEntry hierarchyEntry)
            {
                bool pathStartsWith(string str)
                {
                    return manifestEntry.Path.StartsWith(str, StringComparison.OrdinalIgnoreCase);
                }

                return
                    // All exclusions:
                    !(
                        false

                        || (exclusions.HasFlag(VerificationExclusions.Sfx) && pathStartsWith(@"data\global\sfx\"))
                        || (exclusions.HasFlag(VerificationExclusions.Sfx) && pathStartsWith(@"data\local\sfx\"))

                        || (exclusions.HasFlag(VerificationExclusions.Music) && pathStartsWith(@"data\global\music\"))

                        || (exclusions.HasFlag(VerificationExclusions.Video) && pathStartsWith(@"data\local\video\"))
                    );
            }

            // Verify files against hashes

            var workItems = MpqManifestStatic.Entries
                .Where(entry => entry.Xxh3Hash != null)
                .Select(entry => new
                {
                    manifestEntry = entry,
                    hierarchyEntry = hierarchy.Entries[entry.Path]
                })
                .Where(e => filterPredicate(e.manifestEntry, e.hierarchyEntry))
                .Select(e => new
                {
                    e.manifestEntry,
                    e.hierarchyEntry,

                    mpq = e.hierarchyEntry.MpqLoadedFrom,
                    path = e.manifestEntry.Path,
                    size = e.manifestEntry.Size!.Value,
                    xxh3 = e.manifestEntry.Xxh3Hash,
                })
                // Sort by MPQs to reduce the number of times tasks need to re-open an individual MPQ archive
                .OrderByDescending(e => e.mpq)
                .ToArray();

            int manifestEntryIdx = 0;

            long totalBytesToProcess = workItems.Sum(wi => wi.size);
            long totalBytesProcessed = 0;
            int totalFilesToProcess = workItems.Length;
            int totalFilesProcessed = 0;
            bool errorReported = false;

            progress?.Report(new ProgressValues()
                .Clear()
                .SetTotal(totalBytesProcessed, totalBytesToProcess)
                .SetBytes(totalBytesProcessed, totalBytesToProcess)
                .SetFileCount(totalFilesProcessed, totalFilesToProcess)
                .Extract());

            L.CallerInformation($"Verifying hashes of {totalFilesToProcess:N0}/{MpqManifestStatic.Entries.Length:N0} MPQ manifest entries ({Formatting.FormatSizeInMiB(totalBytesToProcess)} total)...");

            SimpleTimer? updateTimer = null;

            if (progress != null)
            {
                updateTimer = new SimpleTimer(() =>
                {
                    var localTotalBytesProcessed = Interlocked.Read(ref totalBytesProcessed);
                    var localTotalFilesProcessed = totalFilesProcessed;

                    progress.Report(new ProgressValues()
                        .SetTotal(localTotalBytesProcessed, totalBytesToProcess)
                        .SetBytes(localTotalBytesProcessed, totalBytesToProcess)
                        .SetFileCount(localTotalFilesProcessed, totalFilesToProcess)
                        .Extract());
                });
            }

            using (updateTimer)
            {
                var fileVerifyTasks = new Task[Environment.ProcessorCount];

                for (int workerIdx = 0; workerIdx < Environment.ProcessorCount; ++workerIdx)
                {
                    fileVerifyTasks[workerIdx] = Task.Factory.StartNew(() =>
                    {
                        var workItem = workItems.GetDefault();

                        KnownMpq? lastMpq = null;
                        string? lastMpqPath = null;
                        KnownMpq? mpq = null;

                        IntPtr hMpq = IntPtr.Zero;
                        IntPtr hFile = IntPtr.Zero;

                        byte[] readBuffer = ArrayPool<byte>.Shared.Rent(MpqReadBufferSize);
                        List<VerificationFailure> workerFailureList = new(workItems.Length);

                        try
                        {
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                {
                                    if (workItem != null)
                                    {
                                        Interlocked.Increment(ref totalFilesProcessed);
                                    }

                                    var localManifestEntryIdx = Interlocked.Increment(ref manifestEntryIdx) - 1;

                                    if (localManifestEntryIdx >= workItems.Length)
                                    {
                                        // Combine local workerFailureList
                                        lock (failureList)
                                        {
                                            failureList.AddRange(workerFailureList);
                                        }

                                        return;
                                    }

                                    workItem = workItems[localManifestEntryIdx];
                                }

                                lastMpq = mpq;
                                mpq = workItem.mpq;

                                if (lastMpq != mpq)
                                {
                                    if (hMpq != IntPtr.Zero)
                                    {
                                        if (!StormLib.SFileCloseArchive(hMpq))
                                        {
                                            throw new StormLibException(nameof(StormLib.SFileCloseArchive), lastMpqPath!);
                                        }
                                    }

                                    string mpqPath = Path.Combine(hierarchy.MpqDirPath, mpq.Value.ToFileName());

                                    if (!StormLib.SFileOpenArchiveNoListFile(mpqPath, out hMpq))
                                    {
                                        throw new StormLibException(nameof(StormLib.SFileOpenArchiveNoListFile), mpqPath);
                                    }

                                    lastMpq = mpq;
                                    lastMpqPath = mpqPath;
                                }

                                if (!StormLib.SFileOpenFileEx(hMpq, workItem.path, out hFile))
                                {
                                    throw new StormLibException(nameof(StormLib.SFileOpenFileEx), workItem.mpq.ToFileName(), workItem.path);
                                }

                                NonFinalizingXxh3 fileDigest = new();

                                while (StormLib.SFileReadFile(hFile, readBuffer, out uint bytesRead) ||
                                    (StormLib.GetLastError() == StormLib.Error.ERROR_HANDLE_EOF && bytesRead > 0))
                                {
                                    cancellationToken.ThrowIfCancellationRequested();

                                    fileDigest.Update(readBuffer, 0, (int)bytesRead);

                                    {
                                        Interlocked.Add(ref totalBytesProcessed, bytesRead);
                                    }
                                }

                                if (StormLib.GetLastError() != StormLib.Error.ERROR_HANDLE_EOF)
                                {
                                    throw new StormLibException(nameof(StormLib.SFileReadFile), workItem.mpq.ToFileName(), workItem.path);
                                }

                                if (fileDigest.GetHash() != workItem.xxh3)
                                {
                                    L.CallerError($"{workItem.mpq.ToFileName()}: {workItem.path}: {fileDigest.HashName} mismatch");

                                    if (errorIndicatorProgress != null && !errorReported)
                                    {
                                        errorIndicatorProgress.Report(true);
                                        errorReported = true;
                                    }

                                    workerFailureList.Add(new VerificationFailure(VerificationFailure.FailureKind.HashMismatch, workItem.manifestEntry, workItem.hierarchyEntry));
                                }

                                if (!StormLib.SFileCloseFile(hFile))
                                {
                                    throw new StormLibException(nameof(StormLib.SFileCloseFile), workItem.mpq.ToFileName(), workItem.path);
                                }

                                hFile = IntPtr.Zero;
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(readBuffer);

                            if (hFile != IntPtr.Zero)
                            {
                                if (!StormLib.SFileCloseFile(hFile))
                                {
                                    L.CallerError(new StormLibException(nameof(StormLib.SFileCloseFile), workItem?.mpq.ToFileName()!, workItem?.path!), string.Empty);
                                }
                            }

                            if (hMpq != IntPtr.Zero)
                            {
                                if (!StormLib.SFileCloseArchive(hMpq))
                                {
                                    L.CallerError(new StormLibException(nameof(StormLib.SFileCloseArchive), workItem?.mpq.ToFileName()!), string.Empty);
                                }
                            }
                        }
                    }, cancellationToken);
                }

                await Task.WhenAll(fileVerifyTasks).ConfigureAwait(false);
            }

            return failureList;
        }

        private static readonly ImmutableHashSet<string> MpqManifestHashIgnoreList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            // User name and CD-Key related files, potentially unique to every installation.
            @"data\global\chars\am\cof\amblxbow.cof",
            @"data\global\sfx\cursor\curindx.wav",
            @"data\global\sfx\cursor\wavindx.wav",

            // Language file that is already being tested independently by Detect*LanguageVersion()
            @"data\local\use"
        }.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex NonEnglishMpqFilePathRegex = new(
            $@"^data\\local\\.+\\({string.Join('|', LanguageVersionEx.NonEnglishLanguageVersions.Select(lv => lv.To3LetterAbbrev()))})\\",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static async Task<SerializableMpqManifest> CreateMpqManifestAsync(
            MpqHierarchy hierarchy,
            IProgress<ProgressValues.IData>? progress = null,
            int progressUpdateIntervalMilliseconds = UpdateThrottle.DefaultIntervalMilliseconds,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(hierarchy, nameof(hierarchy));

            using LoggedRoutine loggedRoutine = new();

            var workItems = hierarchy.Entries
                .Select(kvp => new
                {
                    path = kvp.Key,
                    mpq = kvp.Value.MpqLoadedFrom,
                    size = kvp.Value.Size
                })
                // Exclude the original installation files (files placed in the archive's root directory)
                .Where(e => e.path.Contains('\\'))
                // Exclude non-English localized assets
                .Where(e => !NonEnglishMpqFilePathRegex.IsMatch(e.path))
                // Exclude MacOS-specific files
                .Where(e => !e.path.StartsWith(@"data\local\macui\", StringComparison.OrdinalIgnoreCase))
                // Sort by MPQs to reduce the number of times tasks need to re-open an individual MPQ archive
                .OrderByDescending(e => e.mpq)
                .ToArray();

            if (workItems.Length == 0)
            {
                throw new InvalidDataException("The resulting MPQ hierarchy is empty.");
            }

            int hierarchyEntryIdx = 0;

            long totalBytesToProcess = workItems.Sum(wi => wi.size);
            long totalBytesProcessed = 0;
            int totalFilesToProcess = workItems.Length;
            int totalFilesProcessed = 0;

            List<MpqManifestEntry> manifestEntries = new(totalFilesToProcess);

            progress?.Report(new ProgressValues()
                .Clear()
                .SetTotal(totalBytesProcessed, totalBytesToProcess)
                .SetBytes(totalBytesProcessed, totalBytesToProcess)
                .SetFileCount(totalFilesProcessed, totalFilesToProcess)
                .Extract());

            L.CallerInformation($"Processing {totalFilesToProcess:N0} MPQ hierarchy entries ({Formatting.FormatSizeInMiB(totalBytesToProcess)} total)...");

            SimpleTimer? updateTimer = null;

            if (progress != null)
            {
                updateTimer = new SimpleTimer(TimeSpan.FromMilliseconds(progressUpdateIntervalMilliseconds), () =>
                {
                    var localTotalBytesProcessed = Interlocked.Read(ref totalBytesProcessed);
                    var localTotalFilesProcessed = totalFilesProcessed;

                    progress.Report(new ProgressValues()
                        .SetTotal(localTotalBytesProcessed, totalBytesToProcess)
                        .SetBytes(localTotalBytesProcessed, totalBytesToProcess)
                        .SetFileCount(localTotalFilesProcessed, totalFilesToProcess)
                        .Extract());
                });
            }

            using (updateTimer)
            {
                var fileVerifyTasks = new Task[Environment.ProcessorCount];

                for (int workerIdx = 0; workerIdx < Environment.ProcessorCount; ++workerIdx)
                {
                    fileVerifyTasks[workerIdx] = Task.Factory.StartNew(() =>
                    {
                        var workItem = workItems.GetDefault();

                        KnownMpq? lastMpq = null;
                        string? lastMpqPath = null;
                        KnownMpq? mpq = null;

                        IntPtr hMpq = IntPtr.Zero;
                        IntPtr hFile = IntPtr.Zero;

                        byte[] readBuffer = ArrayPool<byte>.Shared.Rent(MpqReadBufferSize);
                        List<MpqManifestEntry> workerManifestEntries = new(workItems.Length);

                        try
                        {
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                {
                                    if (workItem != null)
                                    {
                                        Interlocked.Increment(ref totalFilesProcessed);
                                    }

                                    var localHierarchyEntryIdx = Interlocked.Increment(ref hierarchyEntryIdx) - 1;

                                    if (localHierarchyEntryIdx >= workItems.Length)
                                    {
                                        // Combine local workerManifestEntries
                                        lock (manifestEntries)
                                        {
                                            manifestEntries.AddRange(workerManifestEntries);
                                        }

                                        return;
                                    }

                                    workItem = workItems[localHierarchyEntryIdx];
                                }

                                if (MpqManifestHashIgnoreList.Contains(workItem.path))
                                {
                                    workerManifestEntries.Add(new MpqManifestEntry(workItem.path, workItem.mpq, workItem.size));
                                    continue;
                                }

                                lastMpq = mpq;
                                mpq = workItem.mpq;

                                if (lastMpq != mpq)
                                {
                                    if (hMpq != IntPtr.Zero)
                                    {
                                        if (!StormLib.SFileCloseArchive(hMpq))
                                        {
                                            throw new StormLibException(nameof(StormLib.SFileCloseArchive), lastMpqPath!);
                                        }
                                    }

                                    string mpqPath = Path.Combine(hierarchy.MpqDirPath, mpq.Value.ToFileName());

                                    if (!StormLib.SFileOpenArchiveNoListFile(mpqPath, out hMpq))
                                    {
                                        throw new StormLibException(nameof(StormLib.SFileOpenArchiveNoListFile), mpqPath);
                                    }

                                    lastMpq = mpq;
                                    lastMpqPath = mpqPath;
                                }

                                if (!StormLib.SFileOpenFileEx(hMpq, workItem.path, out hFile))
                                {
                                    throw new StormLibException(nameof(StormLib.SFileOpenFileEx), workItem.mpq.ToFileName(), workItem.path);
                                }

                                NonFinalizingXxh3 fileDigest = new();

                                while (StormLib.SFileReadFile(hFile, readBuffer, out uint bytesRead) ||
                                    (StormLib.GetLastError() == StormLib.Error.ERROR_HANDLE_EOF && bytesRead > 0))
                                {
                                    cancellationToken.ThrowIfCancellationRequested();

                                    fileDigest.Update(readBuffer, 0, (int)bytesRead);

                                    {
                                        Interlocked.Add(ref totalBytesProcessed, bytesRead);
                                    }
                                }

                                if (StormLib.GetLastError() != StormLib.Error.ERROR_HANDLE_EOF)
                                {
                                    throw new StormLibException(nameof(StormLib.SFileReadFile), workItem.mpq.ToFileName(), workItem.path);
                                }

                                workerManifestEntries.Add(new MpqManifestEntry(workItem.path, mpq.Value, workItem.size, (Xxh3Hash)fileDigest.GetHash()));

                                if (!StormLib.SFileCloseFile(hFile))
                                {
                                    throw new StormLibException(nameof(StormLib.SFileCloseFile), workItem.mpq.ToFileName(), workItem.path);
                                }

                                hFile = IntPtr.Zero;
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(readBuffer);

                            if (hFile != IntPtr.Zero)
                            {
                                if (!StormLib.SFileCloseFile(hFile))
                                {
                                    L.CallerError(new StormLibException(nameof(StormLib.SFileCloseFile), workItem?.mpq.ToFileName()!, workItem?.path!), string.Empty);
                                }
                            }

                            if (hMpq != IntPtr.Zero)
                            {
                                if (!StormLib.SFileCloseArchive(hMpq))
                                {
                                    L.CallerError(new StormLibException(nameof(StormLib.SFileCloseArchive), workItem?.mpq.ToFileName()!), string.Empty);
                                }
                            }
                        }
                    }, cancellationToken);
                }

                await Task.WhenAll(fileVerifyTasks).ConfigureAwait(false);
            }

            return new SerializableMpqManifest(manifestEntries);
        }
    }
}
