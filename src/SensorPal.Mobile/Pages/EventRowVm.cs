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

    void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
