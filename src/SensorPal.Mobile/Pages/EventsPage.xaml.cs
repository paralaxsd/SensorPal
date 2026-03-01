using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using SensorPal.Mobile.Extensions;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public partial class EventsPage : ContentPage, IQueryAttributable
{
    readonly SensorPalClient _client;
    readonly IAudioManager _audio;
    readonly ConnectivityService _connectivity;
    readonly ILogger<EventsPage> _logger;

    const string SkipEmptyDaysPrefKey = "events_skip_empty_days";

    IAudioPlayer? _player;
    EventRowVm? _currentVm;
    IDispatcherTimer? _timer;
    bool _playbackFailed;
    DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly? _pendingDate;
    bool _hasAppeared;
    SortedSet<DateOnly> _activeDays = [];

    public EventsPage(SensorPalClient client, IAudioManager audio,
        ConnectivityService connectivity, ILogger<EventsPage> logger)
    {
        _client = client;
        _audio = audio;
        _connectivity = connectivity;
        _logger = logger;
        InitializeComponent();
        DateSelector.Date = DateTime.Today;
        SkipEmptyDaysSwitch.IsToggled = Preferences.Get(SkipEmptyDaysPrefKey, false);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("date", out var val)
            || !DateOnly.TryParse(val?.ToString(), out var date)) return;

        // On the very first Shell visit OnAppearing fires before ApplyQueryAttributes,
        // so _hasAppeared is already true — apply immediately. On subsequent visits the
        // order reverses, so store as pending for OnAppearing to pick up.
        if (_hasAppeared)
        {
            DateSelector.Date = date.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            _pendingDate = date;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _hasAppeared = true;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;

        if (SkipEmptyDaysSwitch.IsToggled)
            _ = RefreshActiveDaysAsync();

        if (_pendingDate.HasValue)
        {
            // Setting DateSelector.Date fires OnDateSelected → LoadEventsAsync.
            DateSelector.Date = _pendingDate.Value.ToDateTime(TimeOnly.MinValue);
            _pendingDate = null;
        }
        else
        {
            _ = LoadEventsAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hasAppeared = false;
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
        var current = DateOnly.FromDateTime(DateSelector.Date ?? DateTime.Today);
        if (SkipEmptyDaysSwitch.IsToggled && _activeDays.Count > 0)
        {
            var prev = _activeDays.Reverse().FirstOrDefault(d => d < current);
            if (prev != default)
                DateSelector.Date = prev.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            DateSelector.Date = current.ToDateTime(TimeOnly.MinValue).AddDays(-1);
        }
    }

    void OnNextDayClicked(object? sender, EventArgs e)
    {
        var current = DateOnly.FromDateTime(DateSelector.Date ?? DateTime.Today);
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (SkipEmptyDaysSwitch.IsToggled && _activeDays.Count > 0)
        {
            var next = _activeDays.FirstOrDefault(d => d > current && d <= today);
            if (next != default)
                DateSelector.Date = next.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            if (current < today)
                DateSelector.Date = current.ToDateTime(TimeOnly.MinValue).AddDays(1);
        }
    }

    void OnSkipEmptyDaysToggled(object? sender, ToggledEventArgs e)
    {
        Preferences.Set(SkipEmptyDaysPrefKey, e.Value);
        if (e.Value)
            _ = RefreshActiveDaysAsync();
    }

    async Task RefreshActiveDaysAsync()
    {
        try
        {
            var days = await _client.GetEventDaysAsync();
            _activeDays = new SortedSet<DateOnly>(days);
        }
        catch { _activeDays = []; }
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

    async void OnDeleteEventClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not long id) return;

        var rows = (EventsView.ItemsSource as IEnumerable<EventRowVm>)?.ToList();
        var vm = rows?.FirstOrDefault(r => r.Id == id);
        if (vm is null) return;

        // Stop playback first if this clip is currently playing
        if (_currentVm == vm)
            StopPlayback();

        // Check upfront if this is the last visible clip in the session so the user
        // can back out before anything is actually deleted.
        var isLastInSession = rows?.Count(r => r.SessionId == vm.SessionId) == 1;
        var time = vm.DetectedAt.ToLocalTime().ToString("HH:mm:ss");
        if (!await ConfirmDeleteEventAsync(time, isLastInSession)) return;

        vm.IsDeleting = true;
        try
        {
            var result = await _client.DeleteEventAsync(id);

            if (result?.SessionNowEmpty == true)
                await HandleSessionNowEmptyAsync(result);
            else
                await LoadEventsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete event {Id}", id);
            vm.IsDeleting = false;
        }
    }

    async Task HandleSessionNowEmptyAsync(DeleteEventResultDto result)
    {
        var message = result.SessionHasBackground
            ? "Das war der letzte Clip dieser Session. Die Hintergrundaufnahme (Background-MP3) " +
              "bleibt erhalten.\n\nSoll die gesamte Session inkl. Hintergrundaufnahme ebenfalls " +
              "gelöscht werden?"
            : "Das war der letzte Clip dieser Session. Soll die leere Session ebenfalls gelöscht werden?";

        if (await ConfirmDeleteSessionAsync(message))
        {
            try { await _client.DeleteSessionAsync(result.SessionId); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete empty session {Id}", result.SessionId);
            }
        }

        await LoadEventsAsync();
    }

    Task<bool> ConfirmDeleteEventAsync(string time, bool isLastInSession)
    {
        var message = isLastInSession
            ? $"Clip von {time} löschen?\n\nAchtung: Dies ist der letzte Clip dieser Session. " +
              "Nach dem Löschen verbleiben keine Clips mehr — nur ggf. die Hintergrundaufnahme."
            : $"Clip von {time} löschen?";
        var title = isLastInSession ? "Letzter Clip der Session" : "Clip löschen";
        return this.ConfirmAsync(title, message, "Löschen", "Abbrechen");
    }

    Task<bool> ConfirmDeleteSessionAsync(string message)
        => this.ConfirmAsync("Leere Session löschen?", message, "Session löschen", "Nein, behalten");

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

    Task<bool> ConfirmDeleteDayAsync()
    {
        var message = $"Delete all events and clips for {_selectedDate:dd.MM.yyyy}?";
        return this.ConfirmAsync("Delete Day", message, "Delete", "Cancel");
    }

    async Task LoadEventsAsync()
    {
        try
        {
            var evts = await _client.GetEventsAsync(_selectedDate);
            AuthErrorLabel.IsVisible = _client.IsAuthError;
            EventsView.ItemsSource = evts.Select(e => new EventRowVm(e)).ToList();
        }
        catch
        {
            AuthErrorLabel.IsVisible = _client.IsAuthError;
            EventsView.ItemsSource = Array.Empty<EventRowVm>();
        }
    }
}
