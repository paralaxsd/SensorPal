using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    static readonly int[] BitrateOptions = [32, 64, 96, 128];

    readonly SensorPalClient _client;
    readonly ILogger<SettingsPage> _logger;

    int _preRoll;
    int _postRoll;
    bool _loading;

    public SettingsPage(SensorPalClient client, ILogger<SettingsPage> logger)
    {
        _client = client;
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
        try
        {
            var dto = await _client.GetSettingsAsync();
            if (dto is null) return;

            _loading = true;

            ThresholdSlider.Value = dto.NoiseThresholdDb;
            SetPreRoll(dto.PreRollSeconds);
            SetPostRoll(dto.PostRollSeconds);
            BitratePicker.SelectedIndex = Array.IndexOf(BitrateOptions, dto.BackgroundBitrate)
                is var idx && idx >= 0 ? idx : 1; // default to 64 kbps

            _loading = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
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

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_loading) return;

        SaveButton.IsEnabled = false;
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
