using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using PD2Shared.Logging;
using static PD2Shared.Logging.LoggingStatic;

namespace PD2Shared.Mpq.Internal
{
    internal static class MpqManifestStatic
    {
        public static ImmutableArray<MpqManifestEntry> Entries { get; }

        static MpqManifestStatic()
        {
            var stopwatch = Stopwatch.StartNew();

            var currentAssembly = Assembly.GetCallingAssembly();
            string resourceName = currentAssembly.GetManifestResourceNames().Where(n => n.Contains("mpq.manifest.json")).First();

            using (var resStream = currentAssembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(resStream != null);

                DeserializedMpqManifest manifest = JsonSerializer.Deserialize<DeserializedMpqManifest>(resStream, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    // AllowDuplicateProperties = false // Only available since .NET 10 (https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsondocumentoptions.allowduplicateproperties)
                })!;

                Entries = manifest.Entries
                    .Select(kvp => new MpqManifestEntry(kvp.Key, kvp.Value))
                    .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray();
            }

            L.CallerInformation($"Loaded built-in MPQ manifest with {Entries.Length:N0} entries in {stopwatch.Elapsed}.");
        }
    }
}
