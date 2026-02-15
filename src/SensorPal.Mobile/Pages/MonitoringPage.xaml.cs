using SensorPal.Mobile.Infrastructure;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public partial class MonitoringPage : ContentPage
{
    readonly SensorPalClient _client;
    bool _isMonitoring;
    IDispatcherTimer? _pollTimer;
    int _liveEventCount;

    public MonitoringPage(SensorPalClient client)
    {
        _client = client;
        InitializeComponent();
        _ = LoadSessionsAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadSessionsAsync();
    }

    async void OnToggleClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_isMonitoring)
                await StopMonitoringAsync();
            else
                await StartMonitoringAsync();
        }
        catch
        {
            await DisplayAlertAsync("Error", "Could not reach the server.", "OK");
        }
    }

    async Task StartMonitoringAsync()
    {
        await _client.StartMonitoringAsync();

        _isMonitoring = true;
        _liveEventCount = 0;
        UpdateToggleUi();

        _pollTimer = Dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(2);
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    async Task StopMonitoringAsync()
    {
        _pollTimer?.Stop();
        _pollTimer = null;

        await _client.StopMonitoringAsync();

        _isMonitoring = false;
        UpdateToggleUi();
        await LoadSessionsAsync();
    }

    void OnPollTick(object? sender, EventArgs e) => _ = RefreshLiveStatsAsync();

    async Task RefreshLiveStatsAsync()
    {
        try
        {
            var sessions = await _client.GetSessionsAsync();
            var active = sessions.FirstOrDefault(s => s.IsActive);
            if (active is null) return;

            _liveEventCount = active.EventCount;
            var duration = DateTime.UtcNow - active.StartedAt;

            DurationLabel.Text = duration.ToString(@"hh\:mm\:ss");
            EventCountLabel.Text = active.EventCount.ToString();
        }
        catch { /* silently ignore polling errors */ }
    }

    void UpdateToggleUi()
    {
        StatusLabel.Text = _isMonitoring ? "● Monitoring" : "● Idle";
        StatusLabel.TextColor = _isMonitoring ? Colors.Red : Colors.Gray;
        ToggleButton.Text = _isMonitoring ? "Stop Monitoring" : "Start Monitoring";
        ToggleButton.BackgroundColor = _isMonitoring ? Colors.DarkRed : Colors.DodgerBlue;
        LiveStatsPanel.IsVisible = _isMonitoring;
    }

    async Task LoadSessionsAsync()
    {
        try
        {
            var sessions = await _client.GetSessionsAsync();
            SessionsView.ItemsSource = sessions;
        }
        catch { /* server may not be running */ }
    }
}
