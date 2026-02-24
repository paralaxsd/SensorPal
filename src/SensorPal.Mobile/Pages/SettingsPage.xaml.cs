using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Mobile.Services;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    static readonly int[] BitrateOptions = [32, 64, 96, 128];

    readonly SensorPalClient _client;
    readonly ConnectivityService _connectivity;
    readonly NotificationService _notificationService;
    readonly ILogger<SettingsPage> _logger;

    int _preRoll;
    int _postRoll;
    bool _loading;

    public SettingsPage(
        SensorPalClient client,
        ConnectivityService connectivity,
        NotificationService notificationService,
        ILogger<SettingsPage> logger)
    {
        _client = client;
        _connectivity = connectivity;
        _notificationService = notificationService;
        _logger = logger;
        InitializeComponent();

        BitratePicker.ItemsSource = BitrateOptions.Select(b => $"{b} kbps").ToList();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadSettingsAsync();
    }

    async Task LoadSettingsAsync()
    {
        // Always load local-only settings — no server call needed.
        ServerUrlEntry.Placeholder = _client.ConfiguredBaseUrl;
        ServerUrlEntry.Text = Preferences.Get(PreferencesKeys.ServerUrl, "");
        NotificationsSwitch.IsToggled = _notificationService.IsEnabled;
        ApiKeyEntry.Text = Preferences.Get(PreferencesKeys.ApiKey, "");

        // Server settings — skip if server is unreachable to avoid triggering the offline dialog.
        if (!_connectivity.IsServerReachable) return;

        try
        {
            var dto = await _client.GetSettingsAsync();
            if (dto is null) return;

            _loading = true;
            try
            {
                ThresholdSlider.Value = dto.NoiseThresholdDb;
                SetPreRoll(dto.PreRollSeconds);
                SetPostRoll(dto.PostRollSeconds);
                BitratePicker.SelectedIndex = Array.IndexOf(BitrateOptions, dto.BackgroundBitrate)
                    is var idx && idx >= 0 ? idx : 1;
            }
            finally
            {
                _loading = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load server settings");
        }
    }

    void OnThresholdChanged(object? sender, ValueChangedEventArgs e)
        => ThresholdLabel.Text = $"{e.NewValue:F1} dBFS";

    void OnPreRollMinus(object? sender, EventArgs e) => SetPreRoll(_preRoll - 1);
    void OnPreRollPlus(object? sender, EventArgs e) => SetPreRoll(_preRoll + 1);
    void OnPostRollMinus(object? sender, EventArgs e) => SetPostRoll(_postRoll - 1);
    void OnPostRollPlus(object? sender, EventArgs e) => SetPostRoll(_postRoll + 1);

    void SetPreRoll(int value)
    {
        _preRoll = Math.Clamp(value, 0, 60);
        PreRollLabel.Text = $"{_preRoll}";
    }

    void SetPostRoll(int value)
    {
        _postRoll = Math.Clamp(value, 0, 60);
        PostRollLabel.Text = $"{_postRoll}";
    }

    async void OnNotificationsToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;

        // async void is the only option for event handlers; guard against
        // unhandled exceptions which would otherwise crash the app silently.
        try
        {
            if (e.Value)
            {
                var granted = await _notificationService.TryEnableAsync();
                if (!granted)
                {
                    // Revert the switch without re-triggering this handler.
                    _loading = true;
                    try { NotificationsSwitch.IsToggled = false; }
                    finally { _loading = false; }

                    NotificationsHintLabel.Text =
                        "Notification permission denied. Enable it in system settings.";
                    NotificationsHintLabel.TextColor = Colors.OrangeRed;
                    return;
                }

                NotificationsHintLabel.Text = "Notifications enabled.";
                NotificationsHintLabel.TextColor = Colors.Gray;
            }
            else
            {
                _notificationService.Disable();
                NotificationsHintLabel.Text = "Get notified when a noise event is detected.";
                NotificationsHintLabel.TextColor = Colors.Gray;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle notifications");
            NotificationsHintLabel.Text = "Something went wrong. Please try again.";
            NotificationsHintLabel.TextColor = Colors.OrangeRed;
        }
    }

    void OnCancelClicked(object? sender, EventArgs e)
        => _ = Navigation.PopModalAsync();

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_loading) return;

        SaveButton.IsEnabled = false;
        try
        {
            // Always persist local settings first — these work without server connectivity.
            _client.SetBaseUrl(ServerUrlEntry.Text);
            _client.SetApiKey(ApiKeyEntry.Text.Trim());

            // Try to sync server settings — best effort, don't block dismiss on failure.
            try
            {
                var bitrate = BitratePicker.SelectedIndex >= 0
                    ? BitrateOptions[BitratePicker.SelectedIndex]
                    : 64;

                var dto = new SettingsDto(
                    NoiseThresholdDb: ThresholdSlider.Value,
                    PreRollSeconds: _preRoll,
                    PostRollSeconds: _postRoll,
                    BackgroundBitrate: bitrate);

                await _client.SaveSettingsAsync(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save server settings (local settings were saved)");
            }

            SaveButton.Text = "Saved ✓";
            await Task.Delay(800);
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            SaveButton.Text = "Save failed";
            await Task.Delay(2000);
            SaveButton.Text = "Save";
            SaveButton.IsEnabled = true;
        }
    }
}
