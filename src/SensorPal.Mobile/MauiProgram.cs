using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Shared.Extensions;
using System.Reflection;

namespace SensorPal.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        AddConfiguration(builder);
        RegisterConfigBindings(builder);

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        AddServices(builder);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    static void AddConfiguration(MauiAppBuilder builder)
    {
        AddSettingsConfigFor(builder, "appsettings.json");
        if (OperatingSystem.IsWindows())
        {
            AddSettingsConfigFor(builder, "appsettings.Windows.json");
        }
        else if (OperatingSystem.IsAndroid())
        {
            AddSettingsConfigFor(builder, "appsettings.Android.json");
        }
    }

    static void AddSettingsConfigFor(MauiAppBuilder builder, string resourceName)
    {
        var a = Assembly.GetExecutingAssembly();
        using var stream = a.GetManifestResourceStream("SensorPal.Mobile." + resourceName).NotNull();

        var config = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
        builder.Configuration.AddConfiguration(config);
    }

    static void RegisterConfigBindings(MauiAppBuilder builder)
    {
        builder.Services.Configure<ServerConfig>(
            builder.Configuration.GetRequiredSection(nameof(ServerConfig)));
    }

    static void AddServices(MauiAppBuilder builder)
    {
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SensorPalClient>();
    }
}