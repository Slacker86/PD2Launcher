using System.Text.Json.Serialization;
using PD2Shared.GameFileUpdate;
using PD2Shared.Mpq.Internal;

namespace PD2Shared.Mpq
{
    // Also acts as a "SerializableMpqManifestEntry"
    public class MpqManifestEntry
    {
        internal MpqManifestEntry(string path, KnownMpq expectedMpq, long? size = null, Xxh3Hash? xxh3Hash = null)
        {
            ArgumentNullException.ThrowIfNull(path, nameof(path));

            if (size == null && xxh3Hash != null)
            {
                throw new ArgumentNullException(nameof(size), $"'{nameof(size)}' cannot be null while '{nameof(xxh3Hash)}' is not.");
            }

            Path = path;
            ExpectedMpq = expectedMpq;
            Size = size;
            Xxh3Hash = xxh3Hash;
        }

        internal MpqManifestEntry(string path, DeserializedMpqManifestEntry manifestEntry)
            : this(path, manifestEntry.ExpectedMpq, manifestEntry.Size, manifestEntry.Xxh3Hash)
        {
        }

        [JsonIgnore]
        public string Path { get; }

        [JsonConverter(typeof(KnownMpqJsonConverter))]
        [JsonPropertyName("mpq")]
        public KnownMpq ExpectedMpq { get; }

        public long? Size { get; }

        [JsonConverter(typeof(Xxh3HashJsonConverter))]
        [JsonPropertyName("xxh3")]
        public Xxh3Hash? Xxh3Hash { get; }
    }
}
