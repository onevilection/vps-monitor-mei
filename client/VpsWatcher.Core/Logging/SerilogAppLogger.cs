using Serilog.Core;
using Serilog.Events;

namespace VpsWatcher.Core.Logging;

/// <summary>
/// <see cref="IAppLogger"/> over a Serilog <see cref="Logger"/> (instruction §1). Maps the five
/// severities to Serilog levels, honours the debug_mode threshold via a <see cref="LoggingLevelSwitch"/>,
/// and — critically (§4) — converts an exception to secret-safe fields (<c>reason</c> = type name,
/// optional <c>stack_trace</c>) WITHOUT ever handing the <see cref="Exception"/> to Serilog, so
/// <see cref="Exception.Message"/> (which can embed key path / host) can never reach the sink.
/// </summary>
internal sealed class SerilogAppLogger : IAppLogger
{
    private readonly Logger _logger;
    private readonly bool _includeStackTrace;

    internal SerilogAppLogger(Logger logger, bool includeStackTrace)
    {
        _logger = logger;
        _includeStackTrace = includeStackTrace;
    }

    public bool IsEnabled(LogSeverity severity) => _logger.IsEnabled(ToSerilog(severity));

    public void Log(
        LogSeverity severity,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null,
        Exception? error = null)
    {
        var level = ToSerilog(severity);
        if (!_logger.IsEnabled(level))
            return;

        try
        {
            Serilog.ILogger context = _logger;

            if (properties is not null)
            {
                foreach (var kv in properties)
                    context = context.ForContext(kv.Key, kv.Value, destructureObjects: false);
            }

            if (error is not null)
            {
                // Type name only — never error.Message (§4). Stack trace is opt-in (debug_mode) and
                // still excludes the message.
                context = context.ForContext("reason", error.GetType().Name);
                if (_includeStackTrace && error.StackTrace is { } stack)
                    context = context.ForContext("stack_trace", stack);
            }

            // The exception is deliberately NOT passed here: context.Write(level, ex, …) would render
            // ex.ToString() (including the message) into the event. We pass only the safe fields above.
            context.Write(level, message);
        }
        catch
        {
            // §4: logging must never disrupt the app. Swallow any sink/formatting failure.
        }
    }

    private static LogEventLevel ToSerilog(LogSeverity severity) => severity switch
    {
        LogSeverity.Debug => LogEventLevel.Debug,
        LogSeverity.Info => LogEventLevel.Information,
        LogSeverity.Warning => LogEventLevel.Warning,
        LogSeverity.Error => LogEventLevel.Error,
        LogSeverity.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };

    public void Dispose() => _logger.Dispose();
}
