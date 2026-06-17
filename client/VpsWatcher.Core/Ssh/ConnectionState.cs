using VpsWatcher.Core.Schema;

namespace VpsWatcher.Core.Ssh;

/// <summary>
/// Connection lifecycle states (design §6.1). <see cref="Disconnected"/> means "not reachable,
/// waiting to recover" (auto-reconnect continues); <see cref="HostKeyMismatch"/> means "must
/// NOT connect — actively refused" (auto-reconnect stops). The two are deliberately distinct
/// (§5.4.1).
/// </summary>
public enum ConnectionState
{
    /// <summary>Establishing the SSH session (handshake / host-key check in progress).</summary>
    Connecting,

    /// <summary>Session up, receiving NDJSON.</summary>
    Connected,

    /// <summary>Lost the stream (EOF / error / wall-clock silence). Recovery expected; will reconnect.</summary>
    Disconnected,

    /// <summary>Presented host key did not match the pin. Possible MITM. Auto-reconnect stopped (§5.4.1).</summary>
    HostKeyMismatch,
}

/// <summary>Raised once per successfully parsed NDJSON sample.</summary>
public sealed class MetricsReceivedEventArgs : EventArgs
{
    public MetricsReceivedEventArgs(Sample sample) => Sample = sample;

    public Sample Sample { get; }
}

/// <summary>Raised on every connection-state transition. <see cref="Detail"/> is safe to log (no secrets).</summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionStateChangedEventArgs(ConnectionState oldState, ConnectionState newState, string? detail)
    {
        OldState = oldState;
        NewState = newState;
        Detail = detail;
    }

    public ConnectionState OldState { get; }
    public ConnectionState NewState { get; }

    /// <summary>Human-readable reason, e.g. "connected" or "host key mismatch". Never contains keys/paths.</summary>
    public string? Detail { get; }
}
