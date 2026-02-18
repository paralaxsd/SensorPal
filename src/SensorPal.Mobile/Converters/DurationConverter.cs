using System.Globalization;

namespace SensorPal.Mobile.Converters;

/// <summary>
/// Converts an integer millisecond duration to a human-readable seconds string,
/// e.g. 2341 → "2.3 s".
/// </summary>
sealed class DurationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int ms ? $"{ms / 1000.0:F1} s" : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
