static class MauiBuilderExtensions
{
    public static T If<T>(this T builder, bool condition, Func<T, T> action)
        => condition ? action(builder) : builder;
}
