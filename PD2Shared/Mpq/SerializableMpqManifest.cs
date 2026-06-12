namespace PD2Shared.Mpq
{
    public class SerializableMpqManifest
    {
        internal SerializableMpqManifest(IEnumerable<MpqManifestEntry> entries)
        {
            Entries = new SortedDictionary<string, MpqManifestEntry>(entries.ToDictionary(e => e.Path), StringComparer.OrdinalIgnoreCase);
            Count = Entries.Count;

            if (entries.Count() != Count)
            {
                throw new InvalidDataException($"Every {nameof(MpqManifestEntry)} must have a unique {nameof(MpqManifestEntry.Path)}.");
            }
        }

        public SortedDictionary<string, MpqManifestEntry> Entries { get; }
        public int Count { get; }
    }
}
