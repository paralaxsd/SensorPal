using Microsoft.Extensions.Logging;
using System.Globalization;

namespace SensorPal.Mobile.Converters;

sealed class LogLevelTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogLevel level ? level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        } : "???";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

sealed class LogLevelColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogLevel level ? level switch
        {
            LogLevel.Debug => Colors.BlueViolet,
            LogLevel.Warning => Colors.Orange,
            LogLevel.Error or LogLevel.Critical => Colors.OrangeRed,
            _ => Colors.Gray
        } : Colors.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
