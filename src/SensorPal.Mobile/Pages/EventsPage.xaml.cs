using Plugin.Maui.Audio;
using SensorPal.Mobile.Infrastructure;

namespace SensorPal.Mobile.Pages;

public partial class EventsPage : ContentPage
{
    readonly SensorPalClient _client;
    readonly IAudioManager _audio;
    IAudioPlayer? _player;
    DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);

    public EventsPage(SensorPalClient client, IAudioManager audio)
    {
        _client = client;
        _audio = audio;
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
            var url = _client.GetEventAudioUrl(id);
            PlaybackLabel.Text = "Loading…";

            var stream = await new HttpClient().GetStreamAsync(url);
            _player = _audio.CreatePlayer(stream);
            _player.Play();

            PlaybackLabel.Text = $"Playing event #{id}…";
        }
        catch
        {
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
