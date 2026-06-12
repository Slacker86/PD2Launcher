using System.Numerics;

namespace PD2Shared.Mpq
{
    [Flags]
    public enum VerificationExclusions : uint
    {
#pragma warning disable format
            None          = 0,

            Sfx           = 1 << 0,
            Music         = 1 << 1,
            Video         = 1 << 2,

            Audio         = Sfx | Music,
            AudioVideo    = Audio | Video,
#pragma warning restore format
    }

    // ...and a corresponding extension class:

    public static class VerificationExclusionsEx
    {
        public static string ToPrettyString(this VerificationExclusions exclusions)
        {
            if (exclusions == VerificationExclusions.None)
            {
                return VerificationExclusions.None.ToString();
            }

            return string.Join(" | ", Enum.GetValues<VerificationExclusions>()
                .Where(ve => BitOperations.IsPow2((int)ve))
                .Where(ve => exclusions.HasFlag(ve))
                .Select(ve => ve.ToString()));
        }
    }
}
