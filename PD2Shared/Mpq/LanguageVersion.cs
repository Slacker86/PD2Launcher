using System.Collections.Immutable;

namespace PD2Shared.Mpq
{
    public enum LanguageVersion
    {
        Chinese,
        English,
        French,
        German,
        Italian,
        Japanese,
        Korean,
        Polish,
        Portuguese,
        Russian,
        Spanish,

        Unknown
    }

    // Extension methods:

    public static class LanguageVersionEx
    {
        public static ImmutableArray<LanguageVersion> NonEnglishLanguageVersions { get; } =
            Enum.GetValues<LanguageVersion>()
            .Where(lv => lv != LanguageVersion.English && lv != LanguageVersion.Unknown)
            .ToImmutableArray();

        public static string To3LetterAbbrev(this LanguageVersion languageVersion) => languageVersion switch
        {
#pragma warning disable format
            LanguageVersion.Chinese     => "chi",
            LanguageVersion.English     => "eng",
            LanguageVersion.French      => "fra",
            LanguageVersion.German      => "deu",
            LanguageVersion.Italian     => "ita",
            LanguageVersion.Japanese    => "jpn",
            LanguageVersion.Korean      => "kor",
            LanguageVersion.Polish      => "pol",
            LanguageVersion.Portuguese  => "por",
            LanguageVersion.Russian     => "rus",
            LanguageVersion.Spanish     => "esp",
#pragma warning restore format

            _ => throw new ArgumentException($"Invalid {nameof(LanguageVersion)}: '{languageVersion}'.", nameof(languageVersion))
        };
    }
}
