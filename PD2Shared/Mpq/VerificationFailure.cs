namespace PD2Shared.Mpq
{
    public class VerificationFailure
    {
        public enum FailureKind
        {
            FileNotFound,
            SizeMismatch,
            HashMismatch,
            MpqMismatch,    // Warning
        }

        internal VerificationFailure(FailureKind failureKind, MpqManifestEntry manifestEntry, MpqHierarchy.IEntry? hierarchyEntry)
        {
            Failure = failureKind;
            Path = manifestEntry.Path;
            ExpectedMpq = manifestEntry.ExpectedMpq;
            ActualMpq = hierarchyEntry?.MpqLoadedFrom;
            ExpectedSize = manifestEntry.Size;
            ActualSize = hierarchyEntry?.Size;
        }

        public FailureKind Failure { get; }
        public bool IsError { get => Failure != FailureKind.MpqMismatch; }

        public string Path { get; }

        public KnownMpq ExpectedMpq { get; }
        public KnownMpq? ActualMpq { get; }

        public long? ExpectedSize { get; }
        public long? ActualSize { get; }
    }
}
