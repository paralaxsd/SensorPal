using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Services;

namespace SensorPal.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
            | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        ILogger<MainActivity>? _logger;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _logger = IPlatformApplication.Current?.Services.GetService<ILogger<MainActivity>>();

            // Re-start the foreground service when the app is relaunched after being killed,
            // provided the user had notifications enabled in a previous session.
            MaybeRestartForegroundService();
        }

        void MaybeRestartForegroundService()
        {
            var notificationService = IPlatformApplication.Current?.Services
                .GetService<NotificationService>();

            // Nothing to do if the user never enabled notifications.
            if (notificationService?.IsEnabled != true) return;

            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                _logger?.LogWarning(
                    "Cannot restart notification service: foreground services require " +
                    "Android 8.0 (API 26) or later");
                return;
            }

            var intent = new Intent(this, typeof(SensorPalForegroundService));
            StartForegroundService(intent);
        }
    }
}
