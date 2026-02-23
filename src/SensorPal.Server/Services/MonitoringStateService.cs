namespace SensorPal.Server.Services;

enum MonitoringState { Idle, Monitoring, Calibrating }

sealed class MonitoringStateService
{
    MonitoringState _state = MonitoringState.Idle;

    public MonitoringState State => _state;
    public bool IsMonitoring => _state == MonitoringState.Monitoring;
    public bool IsCalibrating => _state == MonitoringState.Calibrating;

    public event Action? MonitoringStarted;
    public event Action? MonitoringStopped;
    public event Action? CalibrationStarted;
    public event Action? CalibrationStopped;

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

    /// <summary>Only callable from Idle — stop monitoring first if active.</summary>
    public void StartCalibration()
    {
        if (_state != MonitoringState.Idle) return;
        _state = MonitoringState.Calibrating;
        CalibrationStarted?.Invoke();
    }

    public void StopCalibration()
    {
        if (_state != MonitoringState.Calibrating) return;
        _state = MonitoringState.Idle;
        CalibrationStopped?.Invoke();
    }
}
