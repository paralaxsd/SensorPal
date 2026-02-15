using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Mobile.Pages;
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
            .AddAudio()
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
            AddSettingsConfigFor(builder, "appsettings.Windows.json");
        else if (OperatingSystem.IsAndroid())
        {
            AddSettingsConfigFor(builder, "appsettings.Android.json");
            if (DeviceInfo.Current.DeviceType == DeviceType.Virtual)
                AddSettingsConfigFor(builder, "appsettings.Android.Emulator.json");
        }
    }

    static void AddSettingsConfigFor(MauiAppBuilder builder, string resourceName)
    {
        var a = Assembly.GetExecutingAssembly();
        using var stream = a.GetManifestResourceStream("SensorPal.Mobile." + resourceName);
        if (stream is null) return;

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
        builder.Services.AddTransient<MonitoringPage>();
        builder.Services.AddTransient<EventsPage>();
    }
}
