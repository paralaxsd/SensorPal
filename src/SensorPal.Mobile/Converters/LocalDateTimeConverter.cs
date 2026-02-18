using System.Globalization;

namespace SensorPal.Mobile.Converters;

/// <summary>
/// Converts a UTC <see cref="DateTimeOffset"/> to the device's local time zone
/// so XAML StringFormat bindings display local time rather than UTC.
/// </summary>
sealed class LocalDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset dto ? dto.ToLocalTime() : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
