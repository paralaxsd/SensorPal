using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SensorPal.Shared.Extensions;

public static class NullabilityExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T NotNull<T>(this T? obj, string? message = null,
        // https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.callerargumentexpressionattribute
        [CallerArgumentExpression("obj")] string? argumentExpression = null)
        where T : class
    {
        if (obj == null)
        {
            var defaultMsg = $"Argument '{argumentExpression}' of type '{typeof(T)}' must not be null.";
            Throw(message, defaultMsg, argumentExpression);
        }
        return obj;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T NotNull<T>(this T? obj, string? message = null,
        // https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.callerargumentexpressionattribute
        [CallerArgumentExpression("obj")] string? argumentExpression = null)
        where T : struct
    {
        if (obj == null)
        {
            var defaultMsg = $"Argument '{argumentExpression}' of type '{typeof(T)}' must not be null.";
            Throw(message, defaultMsg, argumentExpression);
        }
        return obj.Value;
    }

    [DoesNotReturn]
    static void Throw(string? message, string defaultMessage, string? argumentExpression) =>
        throw new ArgumentException(message ?? defaultMessage,
            message == null ? argumentExpression : null);
}