using SensorPal.Mobile.Extensions;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Mobile.Services;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public partial class MonitoringPage : ContentPage
{
    readonly SensorPalClient _client;
    readonly NotificationService _notificationService;
    bool _isMonitoring;
    bool _isCalibrating;
    DateTimeOffset _monitoringStartedAt;
    IDispatcherTimer? _levelTimer;
    IDispatcherTimer? _blinkTimer;
    bool _blinkOn;
    bool _levelRefreshing;
    string? _autoStopTime;

    public MonitoringPage(SensorPalClient client, NotificationService notificationService)
    {
        _client = client;
        _notificationService = notificationService;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadSessionsAsync();
        _ = LoadAutoStopTimeAsync();
        UpdateNotificationsPausedLabel();
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

    async void OnCalibrateClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_isCalibrating)
                await StopCalibrationAsync();
            else
                await StartCalibrationAsync();
        }
        catch { /* connectivity dialog handles unreachable-server cases */ }
    }

    async Task StartCalibrationAsync()
    {
        await _client.StartCalibrationAsync();
        _isCalibrating = true;
        UpdateToggleUi();
    }

    async Task StopCalibrationAsync()
    {
        await _client.StopCalibrationAsync();
        _isCalibrating = false;
        UpdateToggleUi();
    }

    async Task StartMonitoringAsync()
    {
        await _client.StartMonitoringAsync();

        _isMonitoring = true;
        _monitoringStartedAt = DateTimeOffset.UtcNow;
        _notificationService.OnMonitoringStarted();
        UpdateToggleUi();
        await LoadSessionsAsync();
    }

    async Task StopMonitoringAsync()
    {
        await _client.StopMonitoringAsync();

        _isMonitoring = false;
        _notificationService.OnMonitoringStopped();
        UpdateToggleUi();
        await LoadSessionsAsync();
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

        await SyncServerStateAsync(level);

        AuthErrorLabel.IsVisible = _client.IsAuthError;
        UpdateNotificationsPausedLabel();
        UpdateLevelUi(level);

        // Feed level data to the notification service so Windows (which has no
        // foreground service) can fire toast notifications from the UI poll loop.
        await _notificationService.NotifyIfNewEventAsync(level);
    }

    // Detects every state transition regardless of which client triggered it.
    // Grace period of 2s guards only the Monitoring→Idle direction right after *this*
    // client pressed Start, to let AudioCaptureService initialize.
    async Task SyncServerStateAsync(LiveLevelDto? level)
    {
        if (level is null) return;

        var serverMonitoring = level.ActiveSessionStartedAt.HasValue;
        var serverCalibrating = level.IsCalibrating;
        var stateUnchanged = serverMonitoring == _isMonitoring && serverCalibrating == _isCalibrating;
        var inGracePeriod = _isMonitoring && !serverMonitoring
            && DateTimeOffset.UtcNow - _monitoringStartedAt <= TimeSpan.FromSeconds(2);

        if (stateUnchanged || inGracePeriod) return;

        var wasMonitoring = _isMonitoring;
        _isMonitoring = serverMonitoring;
        _isCalibrating = serverCalibrating;

        if (wasMonitoring && !serverMonitoring)
            _notificationService.OnMonitoringStopped();
        else if (!wasMonitoring && serverMonitoring)
            _notificationService.OnMonitoringStarted();

        UpdateToggleUi();

        if (!serverMonitoring)
            await LoadSessionsAsync();
    }

    void UpdateLevelUi(LiveLevelDto? level)
    {
        if (level?.Db is not { } db)
        {
            LevelLabel.Text = "— dBFS";
            LevelLabel.ClearValue(Label.TextColorProperty);
            LevelBar.Progress = 0;
            ThresholdLabel.Text = level is null ? "Threshold: —" : $"Threshold: {level.ThresholdDb:F1} dBFS";
            EventActiveLabel.IsVisible = false;
            return;
        }

        var aboveThreshold = db >= level.ThresholdDb;

        LevelLabel.Text = $"{db:F1} dBFS";
        if (aboveThreshold)
            LevelLabel.TextColor = Colors.OrangeRed;
        else
            LevelLabel.ClearValue(Label.TextColorProperty);

        LevelBar.Progress = Math.Clamp((db + 90.0) / 90.0, 0.0, 1.0);
        LevelBar.ProgressColor = aboveThreshold ? Colors.OrangeRed : Colors.DodgerBlue;
        ThresholdLabel.Text = $"Threshold: {level.ThresholdDb:F1} dBFS";

        if (level.IsEventActive && level.EventActiveSince is { } since)
        {
            var elapsed = DateTimeOffset.UtcNow - since;
            EventActiveLabel.Text = $"⬤ Detecting event — {elapsed:mm\\:ss}";
            EventActiveLabel.IsVisible = true;
        }
        else
        {
            EventActiveLabel.IsVisible = false;
        }

        if (_isMonitoring && level.ActiveSessionStartedAt is { } sessionStart)
        {
            DurationLabel.Text = (DateTimeOffset.UtcNow - sessionStart).ToString(@"hh\:mm\:ss");
            EventCountLabel.Text = (level.ActiveSessionEventCount ?? 0).ToString();
        }
    }

    // Button visual appearances — defined once, reused across states.
    static readonly (string Text, Color Color)
        BtnToggleStart = ("Start Monitoring", Colors.DodgerBlue),
        BtnToggleStop = ("Stop Monitoring", Colors.DarkRed),
        BtnCalibrateOff = ("Calibrate", Color.FromArgb("#555555")),
        BtnCalibrateOn = ("Stop Calibrating", Colors.DarkOrange);

    readonly record struct PageUiState(
        string StatusText, Color StatusColor,
        (string Text, Color Color) Toggle, bool ToggleEnabled,
        (string Text, Color Color) Calibrate, bool CalibrateEnabled,
        bool LiveStatsVisible, bool Blink);

    static readonly PageUiState IdleUi = new(
        "● Idle", Colors.Gray,
        BtnToggleStart, ToggleEnabled: true,
        BtnCalibrateOff, CalibrateEnabled: true,
        LiveStatsVisible: false, Blink: false);

    static readonly PageUiState MonitoringUi = new(
        "● Monitoring", Colors.Red,
        BtnToggleStop, ToggleEnabled: true,
        BtnCalibrateOff, CalibrateEnabled: false,
        LiveStatsVisible: true, Blink: true);

    static readonly PageUiState CalibratingUi = new(
        "● Calibrating", Colors.DarkOrange,
        BtnToggleStart, ToggleEnabled: false,
        BtnCalibrateOn, CalibrateEnabled: true,
        LiveStatsVisible: false, Blink: true);

    void UpdateToggleUi()
    {
        var ui = _isCalibrating ? CalibratingUi : _isMonitoring ? MonitoringUi : IdleUi;

        StatusLabel.Text = ui.StatusText;
        StatusLabel.TextColor = ui.StatusColor;

        (ToggleButton.Text, ToggleButton.BackgroundColor) = ui.Toggle;
        ToggleButton.IsEnabled = ui.ToggleEnabled;

        (CalibrateButton.Text, CalibrateButton.BackgroundColor) = ui.Calibrate;
        CalibrateButton.IsEnabled = ui.CalibrateEnabled;

        LiveStatsPanel.IsVisible = ui.LiveStatsVisible;

        if (ui.Blink) StartBlinkTimer(); else StopBlinkTimer();
        StatusLabel.Opacity = 1.0;

        UpdateAutoStopLabel();
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

    async void OnPlaySessionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: MonitoringSessionDto session }) { return; }

        await this.ShowSessionPlayerAsync(session, _client.GetSessionAudioUrl(session.Id));
    }

    async void OnSessionInfoClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: MonitoringSessionDto session }) return;
        await this.ShowSessionInfoAsync(session);
    }

    async void OnShowAllSessionsClicked(object? sender, EventArgs e)
    {
        var page = Handler!.MauiContext!.Services.GetRequiredService<SessionsPage>();
        await Navigation.PushModalAsync(page);
    }

    async void OnSessionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MonitoringSessionDto session) return;
        SessionsView.SelectedItem = null;

        var date = session.StartedAt.LocalDateTime.ToString("yyyy-MM-dd");
        await Shell.Current.GoToAsync($"//EventsPage?date={date}");
    }

    async Task LoadSessionsAsync()
    {
        try
        {
            var sessions = await _client.GetSessionsAsync();
            SessionsView.ItemsSource = sessions.Take(4).ToList();
            SessionsLabel.Text = $"Recent Sessions ({sessions.Count} total)";
        }
        catch { /* server may not be running */ }
    }

    async Task LoadAutoStopTimeAsync()
    {
        try
        {
            var settings = await _client.GetSettingsAsync();
            _autoStopTime = settings?.AutoStopTime;
            UpdateAutoStopLabel();
        }
        catch { /* server may not be running */ }
    }

    void UpdateAutoStopLabel()
    {
        var visible = _autoStopTime is { } && _isMonitoring;
        AutoStopLabel.IsVisible = visible;
        if (visible)
            AutoStopLabel.Text = $"Auto-stop at {_autoStopTime}";
    }

    void UpdateNotificationsPausedLabel()
    {
        NotificationsPausedLabel.IsVisible =
            _notificationService.IsEnabled && _notificationService.IsPaused;
    }
}
