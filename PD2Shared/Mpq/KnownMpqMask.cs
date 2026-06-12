using System.Numerics;

namespace PD2Shared.Mpq
{
    [Flags]
    public enum KnownMpqMask : uint
    {
        // Refer to KnownMpq
#pragma warning disable format
        D2video  = KnownMpq.D2video,

        D2data   = KnownMpq.D2data,
        D2sfx    = KnownMpq.D2sfx,
        D2speech = KnownMpq.D2speech,

        D2delta  = KnownMpq.D2delta,
        D2kfixup = KnownMpq.D2kfixup,

        D2char   = KnownMpq.D2char,
        D2music  = KnownMpq.D2music,
        D2exp    = KnownMpq.D2exp,
        D2Xmusic = KnownMpq.D2Xmusic,
        D2Xtalk  = KnownMpq.D2Xtalk,
        D2Xvideo = KnownMpq.D2Xvideo,

        //Patch_d2 = KnownMpq.Patch_d2,
#pragma warning restore format
    }

    // Extension methods:

    public static class KnownMpqMaskEx
    {
        public static KnownMpq GetFinalMpq(this KnownMpqMask knownMpqMask)
        {
            return (KnownMpq)(1 << BitOperations.Log2((uint)knownMpqMask));
        }

        public static KnownMpq[] GetAllMpqs(this KnownMpqMask knownMpqMask)
        {
            return KnownMpqEx.Values
                .Where(mpq => mpq <= knownMpqMask.GetFinalMpq())
                .Where(mpq => knownMpqMask.HasFlag(mpq))
                .Reverse()
                .ToArray();
        }
    }
}
