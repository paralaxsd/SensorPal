namespace SensorPal.Server.Services;

enum MonitoringState { Idle, Monitoring }

sealed class MonitoringStateService
{
    MonitoringState _state = MonitoringState.Idle;

    public MonitoringState State => _state;
    public bool IsMonitoring => _state == MonitoringState.Monitoring;

    public event Action? MonitoringStarted;
    public event Action? MonitoringStopped;

    public void Start()
    {
        if (_state == MonitoringState.Monitoring) return;
        _state = MonitoringState.Monitoring;
        MonitoringStarted?.Invoke();
    }

    public void Stop()
    {
        if (_state == MonitoringState.Idle) return;
        _state = MonitoringState.Idle;
        MonitoringStopped?.Invoke();
    }
}
