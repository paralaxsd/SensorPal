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

    async void OnSessionRowTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not MonitoringSessionDto session) return;

        var date = session.StartedAt.LocalDateTime.ToString("yyyy-MM-dd");
        await Navigation.PopModalAsync();
        await Shell.Current.GoToAsync($"//EventsPage?date={date}");
    }

    async void OnPlaySessionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not MonitoringSessionDto session) return;
        var url = _client.GetSessionAudioUrl(session.Id);
        _logger.LogInformation("Opening player for session {SessionId} ({StartedAt}), url={Url}",
            session.Id, session.StartedAt.LocalDateTime, url);
        await this.ShowSessionPlayerAsync(session, url);
    }

    async void OnDeleteSessionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: MonitoringSessionDto session }) return;
        if (!await ConfirmDeleteSessionAsync(session)) return;

        try
        {
            await _client.DeleteSessionAsync(session.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete session {Id}", session.Id);
        }
    }

    Task<bool> ConfirmDeleteSessionAsync(MonitoringSessionDto session)
    {
        var date = session.StartedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        var message = $"Delete session from {date} including all events and recordings?";
        return this.ConfirmAsync("Delete Session", message, "Delete", "Cancel");
    }

    async void OnCloseClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
