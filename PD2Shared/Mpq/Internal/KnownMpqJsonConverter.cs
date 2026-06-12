using System.Text.Json;
using System.Text.Json.Serialization;

namespace PD2Shared.Mpq.Internal
{
    internal class KnownMpqJsonConverter : JsonConverter<KnownMpq>
    {
        public override KnownMpq Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return KnownMpqEx.Parse(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, KnownMpq value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToFileName());
        }
    }
}
