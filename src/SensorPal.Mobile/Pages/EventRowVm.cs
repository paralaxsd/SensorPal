using System.ComponentModel;
using System.Runtime.CompilerServices;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public sealed class EventRowVm(NoiseEventDto evt) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id => evt.Id;
    public long SessionId => evt.SessionId;
    public DateTimeOffset DetectedAt => evt.DetectedAt;
    public double PeakDb => evt.PeakDb;
    public int DurationMs => evt.DurationMs;
    public int ClipDurationMs => evt.ClipDurationMs;
    public bool HasClip => evt.HasClip;

    bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; Notify(); Notify(nameof(ButtonText)); Notify(nameof(CanInteract)); }
    }

    bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; Notify(); Notify(nameof(ButtonText)); }
    }

    bool _isDeleting;
    public bool IsDeleting
    {
        get => _isDeleting;
        set { _isDeleting = value; Notify(); Notify(nameof(CanInteract)); }
    }

    public string ButtonText => _isLoading ? "⏳" : _isPlaying ? "⏹ Stop" : "▶ Play";
    public bool CanInteract => !_isLoading && !_isDeleting;

    public string ClipOffsetText
    {
        get
        {
            var t = evt.ClipOffsetInSession;
            if (t.TotalHours >= 1)
                return $"+{(int)t.TotalHours}h {t.Minutes}m into session";
            if (t.TotalMinutes >= 1)
                return $"+{(int)t.TotalMinutes}m {t.Seconds}s into session";
            return $"+{(int)t.TotalSeconds}s into session";
        }
    }

    void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
