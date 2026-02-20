using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using SensorPal.Mobile.Infrastructure;

namespace SensorPal.Mobile.Pages;

public partial class EventsPage : ContentPage
{
    readonly SensorPalClient _client;
    readonly IAudioManager _audio;
    readonly ConnectivityService _connectivity;
    readonly ILogger<EventsPage> _logger;

    IAudioPlayer? _player;
    EventRowVm? _currentVm;
    IDispatcherTimer? _timer;
    bool _playbackFailed;
    DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);

    public EventsPage(SensorPalClient client, IAudioManager audio,
        ConnectivityService connectivity, ILogger<EventsPage> logger)
    {
        _client = client;
        _audio = audio;
        _connectivity = connectivity;
        _logger = logger;
        InitializeComponent();
        DateSelector.Date = DateTime.Today;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        _ = LoadEventsAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        StopPlayback();
    }

    void OnConnectivityChanged(bool reachable)
    {
        if (!reachable) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_playbackFailed)
            {
                _playbackFailed = false;
                PlaybackPanel.IsVisible = false;
            }

            _ = LoadEventsAsync();
        });
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

        var rows = EventsView.ItemsSource as IEnumerable<EventRowVm>;
        var vm = rows?.FirstOrDefault(r => r.Id == id);
        if (vm is null) return;

        // Toggle: tapping the playing event stops it
        if (_currentVm == vm)
        {
            StopPlayback();
            return;
        }

        StopPlayback();

        _currentVm = vm;
        vm.IsLoading = true;

        ShowDownloading();

        try
        {
            var stream = await _client.GetEventAudioAsync(id);

            vm.IsLoading = false;
            vm.IsPlaying = true;

            _player = _audio.CreatePlayer(stream);
            _player.Play();

            ShowPlaying();
            StartProgressTimer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback failed for event {Id}", id);
            vm.IsLoading = false;
            _currentVm = null;
            _playbackFailed = true;
            PlaybackLabel.Text = "Playback failed.";
            DownloadSpinner.IsRunning = false;
            DownloadSpinner.IsVisible = false;
        }
    }

    void StopPlayback()
    {
        _timer?.Stop();
        _timer = null;

        _player?.Stop();
        _player?.Dispose();
        _player = null;

        if (_currentVm is not null)
        {
            _currentVm.IsPlaying = false;
            _currentVm.IsLoading = false;
            _currentVm = null;
        }

        _playbackFailed = false;
        PlaybackPanel.IsVisible = false;
    }

    void ShowDownloading()
    {
        PlaybackPanel.IsVisible = true;
        DownloadSpinner.IsVisible = true;
        DownloadSpinner.IsRunning = true;
        PlaybackLabel.Text = "Downloading…";
        PlaybackProgress.IsVisible = false;
    }

    void ShowPlaying()
    {
        DownloadSpinner.IsRunning = false;
        DownloadSpinner.IsVisible = false;
        PlaybackProgress.IsVisible = true;
        PlaybackProgress.Progress = 0;
    }

    void StartProgressTimer()
    {
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    void OnTimerTick(object? sender, EventArgs e)
    {
        if (_player is null || !_player.IsPlaying)
        {
            StopPlayback();
            return;
        }

        var pos = _player.CurrentPosition;
        var dur = _player.Duration;

        PlaybackLabel.Text = $"Playing: {FormatTime(pos)} / {FormatTime(dur)}";

        if (dur > 0)
            PlaybackProgress.Progress = pos / dur;
    }

    static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalMinutes >= 1
            ? $"{(int)t.TotalMinutes}:{t.Seconds:D2}"
            : $"0:{t.Seconds:D2}";
    }

    async void OnDeleteDayClicked(object? sender, EventArgs e)
    {
        if (!await ConfirmDeleteDayAsync()) return;

        StopPlayback();
        try
        {
            await _client.DeleteEventsByDateAsync(_selectedDate);
            await LoadEventsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete events for {Date}", _selectedDate);
        }
    }

    async Task<bool> ConfirmDeleteDayAsync()
    {
        var message = $"Delete all events and clips for {_selectedDate:dd.MM.yyyy}?";
#if ANDROID
        var tcs = new TaskCompletionSource<bool>();
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null) return false;
        activity.RunOnUiThread(() =>
        {
            var dialog = new Android.App.AlertDialog.Builder(activity)
                .SetTitle("Delete Day")!
                .SetMessage(message)!
                .SetPositiveButton("Delete", (_, _) => tcs.TrySetResult(true))!
                .SetNegativeButton("Cancel", (_, _) => tcs.TrySetResult(false))!
                .Create()!;
            dialog.Show();
            var buttonColor = Android.Graphics.Color.Rgb(25, 118, 210);
            dialog.GetButton(-1)?.SetTextColor(buttonColor);
            dialog.GetButton(-2)?.SetTextColor(buttonColor);
        });
        return await tcs.Task;
#else
        return await DisplayAlertAsync("Delete Day", message, "Delete", "Cancel");
#endif
    }

    async Task LoadEventsAsync()
    {
        try
        {
            var evts = await _client.GetEventsAsync(_selectedDate);
            EventsView.ItemsSource = evts.Select(e => new EventRowVm(e)).ToList();
        }
        catch
        {
            EventsView.ItemsSource = Array.Empty<EventRowVm>();
        }
    }
}
