using System.Globalization;

namespace SensorPal.Mobile.Converters;

/// <summary>
/// Converts a UTC <see cref="DateTimeOffset"/> to the device's local time zone and formats it.
/// Set <see cref="Format"/> on the resource instance rather than using ConverterParameter or
/// StringFormat — compiled bindings (x:DataType) drop ConverterParameter and only support
/// StringFormat without literal prefix/suffix text.
/// </summary>
sealed class LocalDateTimeConverter : IValueConverter
{
    /// <summary>DateTimeOffset format string, e.g. "HH:mm:ss" or "dd.MM.yyyy HH:mm".</summary>
    public string Format { get; set; } = "HH:mm:ss";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset dto) return value;
        return dto.ToLocalTime().ToString(Format, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
