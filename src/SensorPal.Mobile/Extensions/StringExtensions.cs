using System.Diagnostics.CodeAnalysis;

namespace SensorPal.Mobile.Extensions;

static class StringExtensions
{
    extension([NotNullWhen(false)] string? text)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(text);
    }

    extension([NotNullWhen(true)] string? text)
    {
        public bool HasContent => !text.IsEmpty;
    }
}