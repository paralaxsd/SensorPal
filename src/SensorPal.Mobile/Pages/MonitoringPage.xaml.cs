using SensorPal.Mobile.Infrastructure;

namespace SensorPal.Mobile.Pages;

public partial class MonitoringPage : ContentPage
{
    readonly SensorPalClient _client;
    bool _isMonitoring;
    IDispatcherTimer? _pollTimer;
    IDispatcherTimer? _levelTimer;
    IDispatcherTimer? _blinkTimer;
    bool _blinkOn;
    int _liveEventCount;
    bool _levelRefreshing;

    public MonitoringPage(SensorPalClient client)
    {
        _client = client;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadSessionsAsync();
        StartLevelTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _levelTimer?.Stop();
        _levelTimer = null;
        StopBlinkTimer();
    }

    void StartLevelTimer()
    {
        _levelTimer = Dispatcher.CreateTimer();
        _levelTimer.Interval = TimeSpan.FromMilliseconds(150);
        _levelTimer.Tick += (_, _) => _ = RefreshLevelAsync();
        _levelTimer.Start();
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
        catch { /* connectivity dialog handles unreachable-server cases */ }
    }

    async Task StartMonitoringAsync()
    {
        await _client.StartMonitoringAsync();

        _isMonitoring = true;
        _liveEventCount = 0;
        UpdateToggleUi();
        await LoadSessionsAsync();

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
            var duration = DateTimeOffset.UtcNow - active.StartedAt;

            DurationLabel.Text = duration.ToString(@"hh\:mm\:ss");
            EventCountLabel.Text = active.EventCount.ToString();
        }
        catch { /* silently ignore polling errors */ }
    }

    async Task RefreshLevelAsync()
    {
        if (_levelRefreshing) return;
        _levelRefreshing = true;
        try { await DoRefreshLevelAsync(); }
        finally { _levelRefreshing = false; }
    }

    async Task DoRefreshLevelAsync()
    {
        var level = await _client.GetLevelAsync();

        if (level?.Db is not { } db)
        {
            LevelLabel.Text = "— dBFS";
            LevelBar.Progress = 0;
            ThresholdLabel.Text = level is null ? "Threshold: —" : $"Threshold: {level.ThresholdDb:F1} dBFS";
            return;
        }

        var aboveThreshold = db >= level.ThresholdDb;
        var progress = Math.Clamp((db + 60.0) / 60.0, 0.0, 1.0);

        LevelLabel.Text = $"{db:F1} dBFS";
        if (aboveThreshold)
            LevelLabel.TextColor = Colors.OrangeRed;
        else
            LevelLabel.ClearValue(Label.TextColorProperty);
        LevelBar.Progress = progress;
        LevelBar.ProgressColor = aboveThreshold ? Colors.OrangeRed : Colors.DodgerBlue;
        ThresholdLabel.Text = $"Threshold: {level.ThresholdDb:F1} dBFS";

        if (level.IsEventActive && level.EventActiveSince.HasValue)
        {
            var elapsed = DateTimeOffset.UtcNow - level.EventActiveSince.Value;
            EventActiveLabel.Text = $"⬤ Detecting event — {elapsed:mm\\:ss}";
            EventActiveLabel.IsVisible = true;
        }
        else
        {
            EventActiveLabel.IsVisible = false;
        }
    }

    void UpdateToggleUi()
    {
        StatusLabel.Text = _isMonitoring ? "● Monitoring" : "● Idle";
        StatusLabel.TextColor = _isMonitoring ? Colors.Red : Colors.Gray;
        StatusLabel.Opacity = 1.0;
        ToggleButton.Text = _isMonitoring ? "Stop Monitoring" : "Start Monitoring";
        ToggleButton.BackgroundColor = _isMonitoring ? Colors.DarkRed : Colors.DodgerBlue;
        LiveStatsPanel.IsVisible = _isMonitoring;

        if (_isMonitoring)
            StartBlinkTimer();
        else
            StopBlinkTimer();
    }

    void StartBlinkTimer()
    {
        if (_blinkTimer is { }) return;
        _blinkTimer = Dispatcher.CreateTimer();
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(600);
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            StatusLabel.Opacity = _blinkOn ? 1.0 : 0.15;
        };
        _blinkTimer.Start();
    }

    void StopBlinkTimer()
    {
        _blinkTimer?.Stop();
        _blinkTimer = null;
        StatusLabel.Opacity = 1.0;
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
