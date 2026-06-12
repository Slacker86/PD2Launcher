using System.Text.Json.Serialization;

namespace PD2Shared.Mpq.Internal
{
    internal class DeserializedMpqManifest
    {
        [JsonConstructor]
        public DeserializedMpqManifest(Dictionary<string, DeserializedMpqManifestEntry> entries, int count)
        {
            Entries = entries;
            Count = count;

            if (Entries.Count != Count)
            {
                throw new InvalidDataException($"Actual JSON entry count ({Entries.Count}) does not match the manifest ({Count}).");
            }

            if (Entries.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Entries.Count)
            {
                throw new InvalidDataException("Every entry must have a unique path.");
            }
        }

        public Dictionary<string, DeserializedMpqManifestEntry> Entries { get; }
        public int Count { get; }
    }
}
