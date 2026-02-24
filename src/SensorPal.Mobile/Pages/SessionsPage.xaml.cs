using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Extensions;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public partial class SessionsPage : ContentPage
{
    readonly SensorPalClient _client;
    readonly ILogger<SessionsPage> _logger;

    public SessionsPage(SensorPalClient client, ILogger<SessionsPage> logger)
    {
        _client = client;
        _logger = logger;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        try
        {
            var sessions = await _client.GetSessionsAsync();
            SessionsView.ItemsSource = sessions;
        }
        catch { /* server may not be running */ }
    }

    void OnSessionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MonitoringSessionDto session) return;
        SessionsView.SelectedItem = null;

        // Defer navigation so the SelectionChanged event fully unwinds before
        // PopModalAsync destroys the PlatformView — otherwise MAUI crashes trying
        // to update the CollectionView selection state on a null native view.
        var date = session.StartedAt.LocalDateTime.ToString("yyyy-MM-dd");
        Dispatcher.Dispatch(async () =>
        {
            await Navigation.PopModalAsync();
            await Shell.Current.GoToAsync($"//EventsPage?date={date}");
        });
    }

    async void OnPlaySessionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not MonitoringSessionDto session) return;
        var url = _client.GetSessionAudioUrl(session.Id);
        _logger.LogInformation("Opening player for session {SessionId} ({StartedAt}), url={Url}",
            session.Id, session.StartedAt.LocalDateTime, url);
        await this.ShowSessionPlayerAsync(session, url);
    }

    async void OnCloseClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
