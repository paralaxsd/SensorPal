namespace SensorPal.Server.Services;

enum MonitoringState { Idle, Monitoring, Calibrating }

sealed class MonitoringStateService
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    MonitoringState _state = MonitoringState.Idle;

    /******************************************************************************************
     * PROPERTIES
     * ***************************************************************************************/
    public MonitoringState State => _state;
    public bool IsMonitoring => _state == MonitoringState.Monitoring;
    public bool IsCalibrating => _state == MonitoringState.Calibrating;

    /******************************************************************************************
     * EVENTS
     * ***************************************************************************************/
    public event Action<MonitoringState>? StateChanged;

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public void Start()
    {
        if (_state != MonitoringState.Monitoring) { TransitionTo(MonitoringState.Monitoring); }
    }
    public void Stop()
    {
        if (_state != MonitoringState.Idle) { TransitionTo(MonitoringState.Idle); }
    }

    /// <summary>
    /// Only callable from Idle — stop monitoring first if active.
    /// </summary>
    public void StartCalibration()
    {
        if (_state == MonitoringState.Idle) { TransitionTo(MonitoringState.Calibrating); }
    }
    public void StopCalibration()
    {
        if (_state == MonitoringState.Calibrating) { TransitionTo(MonitoringState.Idle); }
    }

    void TransitionTo(MonitoringState next) { _state = next; StateChanged?.Invoke(next); }
}
