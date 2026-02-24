using System.Diagnostics.CodeAnalysis;

namespace SensorPal.Shared.Extensions;

public static class StringExtensions
{
    extension([NotNullWhen(false)] string? text)
    {
        public bool NullOrWhitespace => string.IsNullOrWhiteSpace(text);
        public bool NullOrEmpty => string.IsNullOrEmpty(text);
    }

    extension([NotNullWhen(true)] string? text)
    {
        public bool HasContent => !text.NullOrWhitespace;
    }
}