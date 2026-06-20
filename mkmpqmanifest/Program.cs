using System.Text.Json;
using PD2Shared.GameFileUpdate;
using PD2Shared.Mpq;
using PD2Shared.Utils;
using PD2Shared.Logging;
using static PD2Shared.Logging.LoggingStatic;
using Serilog;

namespace mkmpqmanifest
{
    internal class Program
    {
        private const string ConsoleOutputTemplate = "{Timestamp:HH:mm:ss.fff} {Level:u1}{Level:u1} {Message:lj}{NewLine}{Exception}";

        private static readonly object _logSyncObject = new();

        private static void UpdateProgress(ProgressValues.IData progressData)
        {
            if (progressData.Total == null || progressData.Total == 0)
            {
                return;
            }

            L.CallerDebug($"{progressData.Total.Value * 100:N1}%");
        }

        static async Task Main()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Async(c => c.Console(
                    syncRoot: _logSyncObject,
                    outputTemplate: ConsoleOutputTemplate,
                    standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose))
                .CreateLogger();

            L.CallerInformation($"Using up to {Environment.ProcessorCount} concurrent task(s)");

            if (Mpq.StormLibVersionString != null)
            {
                L.CallerInformation($"Using StormLib {Mpq.StormLibVersionString}");
            }
            else
            {
                L.CallerWarning("Using an undetermined StormLib version");
            }

            MpqHierarchy hierarchy = MpqHierarchy.Create(Env.GetCwd());
            SerializableMpqManifest manifest = await Mpq.CreateMpqManifestAsync(
                hierarchy,
                new DirectProgress<ProgressValues.IData>(UpdateProgress),
                progressUpdateIntervalMilliseconds: 250);

            using var outStream = Console.OpenStandardOutput();

            JsonSerializer.Serialize(outStream, manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
    }
}
