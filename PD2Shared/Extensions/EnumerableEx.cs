namespace PD2Shared.Extensions
{
    public static class EnumerableEx
    {
        public static TSource? GetDefault<TSource>(this IEnumerable<TSource> source) => (TSource?)default;
    }
}
