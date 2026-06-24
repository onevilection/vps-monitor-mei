namespace VpsWatcher.Core.Alerts;

/// <summary>
/// Overall gadget state for one server (design §6.1). The integer order is the priority order, so
/// worst-of aggregation is a plain <c>Math.Max</c>: a metric band (Normal..Critical) is outranked by
/// <see cref="Disconnected"/> (metrics are stale / unjudgeable), which is in turn outranked by
/// <see cref="HostKeyMismatch"/> (MITM suspicion, top priority — §5.4.1/§6.1).
/// </summary>
public enum AlertLevel
{
    Normal = 0,
    Caution = 1,
    Warning = 2,
    Critical = 3,

    /// <summary>Connection lost; metrics not judgeable. Outranks any metric band.</summary>
    Disconnected = 4,

    /// <summary>Host key did not match the pin. Top priority (§5.4.1/§6.1).</summary>
    HostKeyMismatch = 5,
}
