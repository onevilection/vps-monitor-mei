namespace VpsWatcher.Core.Alerts;

/// <summary>Raised on every overall-state transition (design §6). Carries no secrets — server id,
/// state names, the driving metric (<see cref="Cause"/>) and its value only.</summary>
public sealed class AlertStateChangedEventArgs : EventArgs
{
    public AlertStateChangedEventArgs(AlertLevel oldState, AlertLevel newState, string? cause, double? value)
    {
        OldState = oldState;
        NewState = newState;
        Cause = cause;
        Value = value;
    }

    public AlertLevel OldState { get; }
    public AlertLevel NewState { get; }

    /// <summary>Which input drove the new state: <c>cpu</c>/<c>mem</c>/<c>swap</c>/<c>disk:&lt;mount&gt;</c>/<c>connection</c>.</summary>
    public string? Cause { get; }

    /// <summary>The driving metric's value (%), or null for connection-derived states.</summary>
    public double? Value { get; }
}

/// <summary>Raised when an escalation should trigger a voice alert — i.e. it passed the escalation
/// and cooldown gates (design §6.4). The actual playback lives in a later phase; this is "should it
/// sound?" only.</summary>
public sealed class AlertTriggeredEventArgs : EventArgs
{
    public AlertTriggeredEventArgs(AlertLevel level, string? cause, double? value)
    {
        Level = level;
        Cause = cause;
        Value = value;
    }

    public AlertLevel Level { get; }
    public string? Cause { get; }
    public double? Value { get; }
}
