using System.Text.Json.Serialization;
using PD2Shared.GameFileUpdate;

namespace PD2Shared.Mpq.Internal
{
    internal class DeserializedMpqManifestEntry
    {
        [JsonConstructor]
        public DeserializedMpqManifestEntry(KnownMpq expectedMpq, long? size, Xxh3Hash? xxh3Hash)
        {
            if (size == null && xxh3Hash != null)
            {
                throw new ArgumentNullException(nameof(size), $"'{nameof(size)}' cannot be null while '{nameof(xxh3Hash)}' is not.");
            }

            ExpectedMpq = expectedMpq;
            Size = size;
            Xxh3Hash = xxh3Hash;
        }

        [JsonConverter(typeof(KnownMpqJsonConverter))]
        [JsonPropertyName("mpq")]
        public KnownMpq ExpectedMpq { get; }

        public long? Size { get; }

        [JsonConverter(typeof(Xxh3HashJsonConverter))]
        [JsonPropertyName("xxh3")]
        public Xxh3Hash? Xxh3Hash { get; }
    }
}
