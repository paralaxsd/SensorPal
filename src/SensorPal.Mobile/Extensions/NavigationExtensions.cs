using SensorPal.Mobile.Pages;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Extensions;

static class NavigationExtensions
{
    extension(Page page)
    {
        /// <summary>
        /// Resolves <see cref="SessionPlayerPage"/> from DI, loads the given session,
        /// and pushes it modally onto the navigation stack.
        /// </summary>
        public Task ShowSessionPlayerAsync(MonitoringSessionDto session, string url)
        {
            var player = page.Handler!.MauiContext!.Services.GetRequiredService<SessionPlayerPage>();
            player.Load(session, url);
            return page.Navigation.PushModalAsync(player);
        }
    }
}
