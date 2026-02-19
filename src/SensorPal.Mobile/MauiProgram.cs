using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Mobile.Logging;
using SensorPal.Mobile.Pages;
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

        AddLogging(builder);
        AddServices(builder);

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

    static void AddLogging(MauiAppBuilder builder)
    {
#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        AddMemoryLogger(builder);
    }

    static void AddMemoryLogger(MauiAppBuilder builder)
    {
        // Create the log store upfront so it can be shared between the
        // logging pipeline and the DI container.

        var logStore = new InMemoryLogStore();
        builder.Services.AddSingleton(logStore);
#if DEBUG
        var minLevel = LogLevel.Debug;
#else
        var minLevel = LogLevel.Information;
#endif
        builder.Logging.AddProvider(new InMemoryLoggerProvider(logStore, minLevel));
    }

    static void AddServices(MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ConnectivityService>();
        builder.Services.AddSingleton<ConnectivityDialogService>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SensorPalClient>();
        builder.Services.AddTransient<MonitoringPage>();
        builder.Services.AddTransient<EventsPage>();
        builder.Services.AddTransient<LogsPage>();
        builder.Services.AddTransient<SettingsPage>();
    }
}
