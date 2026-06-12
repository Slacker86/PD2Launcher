using System.Collections.Immutable;

namespace PD2Shared.Mpq
{
    public enum KnownMpq : uint
    {
        // Sorted by their load order
        //
        // Worked out partly thanks to https://github.com/ThePhrozenKeep/D2MOO/blob/master/source/D2Win/src/D2WinArchive.cpp

#pragma warning disable format
        // Technically d2video.mpq is never loaded when running as expansion unless a pre-expansion video needs to be played.
        // As such, place this file at the very start, implying it can be effectively overridden by all the other archives.
        D2video  = 1 <<  0,

        D2data   = 1 <<  1,
        D2sfx    = 1 <<  2,
        D2speech = 1 <<  3,

        // These are non-standard and not supplied with the base game, but the game still attempts to load them.
        D2delta  = 1 <<  4,
        D2kfixup = 1 <<  5,

        D2char   = 1 <<  6,
        D2music  = 1 <<  7,
        D2exp    = 1 <<  8,
        D2Xmusic = 1 <<  9,
        D2Xtalk  = 1 << 10,
        D2Xvideo = 1 << 11,

        // The OG patch_d2.mpq is never loaded. Instead, the PD2-supplied one takes its place.
        //Patch_d2 = 1 << 12,
#pragma warning restore format
    }

    // Extension methods:

    public static class KnownMpqEx
    {
        private static readonly ImmutableArray<KnownMpq> _valuesArray;
        private static readonly ImmutableHashSet<KnownMpq> _valuesHashSet;

        static KnownMpqEx()
        {
            _valuesArray = Enum.GetValues<KnownMpq>().ToImmutableArray();
            _valuesHashSet = _valuesArray.ToImmutableHashSet();

            if (_valuesHashSet.Count != _valuesArray.Length)
            {
                throw new InvalidDataException($"{nameof(KnownMpq)} values must be unique.");
            }
        }

        public static ImmutableArray<KnownMpq> Values { get => _valuesArray; }

        private static void CheckEnumValue(KnownMpq knownMpq)
        {
            if (!_valuesHashSet.Contains(knownMpq))
            {
                throw new ArgumentException($"'{nameof(knownMpq)}' does not map to any of the values of {nameof(KnownMpq)}.", nameof(knownMpq));
            }
        }

        public static KnownMpq? TryParse(string str)
        {
            foreach (var knownMpq in _valuesArray)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(str, knownMpq.ToFileName()) == 0)
                {
                    return knownMpq;
                }
            }

            return null;
        }

        public static KnownMpq Parse(string str)
        {
            var res = TryParse(str);

            if (res == null)
            {
                throw new ArgumentException($"Failed to parse '{str}' as a '{nameof(KnownMpq)}'.", nameof(str));
            }

            return res.Value;
        }

        public static string ToFileName(this KnownMpq knownMpq)
        {
            CheckEnumValue(knownMpq);

            return Enum.GetName<KnownMpq>(knownMpq)!.ToLowerInvariant() + ".mpq";
        }

        public static bool IsNonStandard(this KnownMpq knownMpq)
        {
            CheckEnumValue(knownMpq);

            return knownMpq == KnownMpq.D2kfixup || knownMpq == KnownMpq.D2delta;
        }
    }
}
