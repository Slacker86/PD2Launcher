using System.Diagnostics;
using System.Reflection;
using PD2Shared.Logging;
using static PD2Shared.Logging.LoggingStatic;

namespace PD2Shared.Mpq.Internal
{
    internal static class ListFileStatic
    {
        // The most complete listfile, courtesy of The Phrozen Keep.
        // (https://d2mods.info/forum/viewtopic.php?t=65218)
        public static string[] ListFile { get; }

        static ListFileStatic()
        {
            var stopwatch = Stopwatch.StartNew();

            var currentAssembly = Assembly.GetCallingAssembly();

            string resourceName = currentAssembly.GetManifestResourceNames().Where(n => n.Contains("listfile.txt")).First();

            int entryCount = 0;

            // ArrayBuilder<T> is only available in Microsoft.VisualStudio.Utilities, so count the number of entries first...
            using (var resStream = currentAssembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(resStream != null);

                using var reader = new StreamReader(resStream);

                string? line;

                // Since counting newlines alone might not cut it, just go through the file one additional time.
                while ((line = reader.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        ++entryCount;
                    }
                }
            }

            ListFile = new string[entryCount];
            int entryIdx = 0;

            // ...and then load up the entries
            using (var resStream = currentAssembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(resStream != null);

                using var reader = new StreamReader(resStream);

                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        ListFile[entryIdx++] = line;
                    }
                }
            }

            L.CallerInformation($"Loaded built-in {StormLib.MetadataListFilePath} with {ListFile.Length:N0} entries in {stopwatch.Elapsed}.");
        }
    }
}
