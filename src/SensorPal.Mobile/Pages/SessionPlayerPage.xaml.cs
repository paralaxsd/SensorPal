using CommunityToolkit.Maui.Views;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

// ReSharper disable once RedundantExtendsListEntry
public partial class SessionPlayerPage : ContentPage
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    readonly SensorPalClient _client;

    DateTimeOffset _sessionStart;
    CancellationTokenSource? _timerCts;

    /******************************************************************************************
     * STRUCTORS
     * ***************************************************************************************/
    public SessionPlayerPage(SensorPalClient client)
    {
        InitializeComponent();
        _client = client;
    }

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public void Load(MonitoringSessionDto session, string audioUrl)
    {
        _sessionStart = session.StartedAt;
        TitleLabel.Text = session.StartedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        WallClockLabel.Text = session.StartedAt.LocalDateTime.ToString("HH:mm:ss");
        Player.Source = MediaSource.FromUri(audioUrl);
        _ = LoadMarkersAsync(session.Id);
        }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartWallClockTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
        Player.Stop();
    }

    async void OnCloseClicked(object? sender, EventArgs e)
    {
        Player.Stop();
        await Navigation.PopModalAsync();
    }

    async Task LoadMarkersAsync(long sessionId)
    {
        try
        {
            var markers = await _client.GetSessionMarkersAsync(sessionId);
            if (markers.Count == 0) return;

            foreach (var marker in markers)
            {
                var btn = new Button
                {
                    Text = $"{marker.DetectedAt.LocalDateTime:HH:mm:ss}\n{marker.PeakDb:F1} dB",
                    FontSize = 11,
                    LineBreakMode = LineBreakMode.NoWrap,
                    Padding = new Thickness(10, 6),
                    CornerRadius = 16,
                };
                var captured = marker;
                btn.Clicked += (_, _) => Player.SeekTo(TimeSpan.FromSeconds(captured.OffsetSeconds));
                MarkersLayout.Children.Add(btn);
            }

            MarkersScroll.IsVisible = true;
        }
        catch
        {
            // markers are optional — don't crash if unavailable
        }
    }

    void StartWallClockTimer()
    {
        _timerCts?.Cancel();
        _timerCts = new CancellationTokenSource();
        var token = _timerCts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var wallTime = _sessionStart.LocalDateTime + Player.Position;
                        WallClockLabel.Text = wallTime.ToString("HH:mm:ss");
                    });
                }
            }
            catch (OperationCanceledException) { }
        });
    }
}
