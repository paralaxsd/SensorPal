using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using SensorPal.Mobile.Infrastructure;

namespace SensorPal.Mobile.Pages;

public partial class EventsPage : ContentPage
{
    readonly SensorPalClient _client;
    readonly IAudioManager _audio;
    readonly ILogger<EventsPage> _logger;
    IAudioPlayer? _player;
    DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);

    public EventsPage(SensorPalClient client, IAudioManager audio, ILogger<EventsPage> logger)
    {
        _client = client;
        _audio = audio;
        _logger = logger;
        InitializeComponent();
        DateSelector.Date = DateTime.Today;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadEventsAsync();
    }

    void OnDateSelected(object? sender, DateChangedEventArgs e)
    {
        _selectedDate = DateOnly.FromDateTime(e.NewDate ?? DateTime.Today);
        _ = LoadEventsAsync();
    }

    void OnPrevDayClicked(object? sender, EventArgs e)
    {
        var current = DateSelector.Date ?? DateTime.Today;
        DateSelector.Date = current.AddDays(-1);
    }

    void OnNextDayClicked(object? sender, EventArgs e)
    {
        var current = DateSelector.Date ?? DateTime.Today;
        if (current.Date < DateTime.Today)
            DateSelector.Date = current.AddDays(1);
    }

    async void OnPlayClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not long id) return;

        _player?.Stop();
        _player?.Dispose();

        try
        {
            PlaybackLabel.Text = "Loading…";

            var stream = await _client.GetEventAudioAsync(id);
            _player = _audio.CreatePlayer(stream);
            _player.Play();

            PlaybackLabel.Text = $"Playing event #{id}…";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback failed for event {Id}", id);
            PlaybackLabel.Text = "Playback failed.";
        }
    }

    async Task LoadEventsAsync()
    {
        try
        {
            var evts = await _client.GetEventsAsync(_selectedDate);
            EventsView.ItemsSource = evts;
        }
        catch
        {
            EventsView.ItemsSource = Array.Empty<object>();
        }
    }
}
