namespace VpsWatcher.Core.Logging;

/// <summary>
/// Generic, app-agnostic structured logger (instruction §1). Implementations write one NDJSON record
/// per call. This layer owns the cross-cutting discipline — severity normalisation, the debug_mode
/// threshold, and the secret-safe handling of exceptions — but knows nothing about VpsWatcher's
/// domain (SSH, state machine, …). Each call site decides which event to record at which level.
/// </summary>
public interface IAppLogger : IDisposable
{
    /// <summary>
    /// True if an event at <paramref name="severity"/> would be recorded under the current
    /// debug_mode threshold. Call sites use this to skip building per-event context (e.g. the 1 Hz
    /// heartbeat) when it would be dropped anyway — preserving the low-load budget (§13.2).
    /// </summary>
    bool IsEnabled(LogSeverity severity);

    /// <summary>
    /// Records one structured event. <paramref name="properties"/> become independent top-level
    /// fields (so <c>jq 'select(.server_id=="…")'</c> works). When <paramref name="error"/> is
    /// supplied, only its type name (field <c>reason</c>) — and, in debug_mode, its stack trace
    /// (field <c>stack_trace</c>) — are recorded; <see cref="Exception.Message"/> is NEVER written,
    /// because SSH.NET messages can embed the key path / host (§4). A logging failure must never
    /// surface to the caller.
    /// </summary>
    void Log(
        LogSeverity severity,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null,
        Exception? error = null);
}
