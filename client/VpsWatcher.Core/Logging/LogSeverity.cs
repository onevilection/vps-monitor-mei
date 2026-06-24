namespace VpsWatcher.Core.Logging;

/// <summary>
/// The five severity levels recorded by the app (ov-logger style). Serilog's six internal levels are
/// normalised onto these five: Verbose/Debug → <see cref="Debug"/>, Information → <see cref="Info"/>,
/// Fatal → <see cref="Critical"/>. This is the only severity vocabulary callers see; it is generic and
/// carries no app-specific meaning.
/// </summary>
public enum LogSeverity
{
    /// <summary>Verbose detail; recorded only when debug_mode is ON.</summary>
    Debug,

    /// <summary>Normal lifecycle events (start/stop, connected). The OFF-mode threshold.</summary>
    Info,

    /// <summary>Recoverable trouble (disconnect, retry, watchdog fire).</summary>
    Warning,

    /// <summary>An operation failed (init failure, unexpected exception).</summary>
    Error,

    /// <summary>Most severe — security-relevant or unrecoverable (e.g. host-key mismatch).</summary>
    Critical,
}
