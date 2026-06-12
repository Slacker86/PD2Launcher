using PD2Shared.Mpq.Internal;

namespace PD2Shared.Mpq
{
    public class StormLibException : Exception
    {
        internal StormLibException(StormLib.Error error, string functionName, string paramName, params string[] paramNames)
            // Multi-line string interpolation expressions are only available since .NET 7, *sigh*...
            : base($"{functionName}({(string.Join(", ", new string[] { paramName }.Concat(paramNames).Where(p => p != null).Select(p => $"'{p}'")))}) failed with {StormLib.GetLastErrorMessage(error)}.")
        {
        }

        internal StormLibException(string functionName, string paramName, params string[] paramNames)
            : this(StormLib.GetLastError(), functionName, paramName, paramNames)
        {
        }
    }
}
