using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace VpsWatcher.Core.Logging;

/// <summary>
/// Factory for the generic NDJSON file logger (instruction §1/§3). Everything app-specific is
/// injected: <c>appName</c> (recorded as the <c>app</c> field and used as the file-name prefix) and
/// <c>logDirectory</c> (where to write). Daily rotation with 14-day retention mirrors the ov-logger
/// rotate/maxage policy (§3); the debug_mode flag sets the minimum level (OFF = Info+, ON = Debug+).
/// </summary>
public static class AppLogger
{
    // Defensive ceiling so a runaway loop can't fill the disk; rotates within a day if exceeded.
    private const long FileSizeLimitBytes = 64L * 1024 * 1024;
    private const int RetainedFileCountLimit = 14;

    /// <summary>
    /// Creates a file-backed logger writing <c>{appName-lowercase}-YYYYMMDD.ndjson</c> into
    /// <paramref name="logDirectory"/>. If the directory/sink cannot be created the call still
    /// succeeds, returning a no-op logger — logging must never break the app (§4).
    /// </summary>
    public static IAppLogger CreateFile(string appName, bool debugMode, string logDirectory)
    {
        if (string.IsNullOrWhiteSpace(appName))
            throw new ArgumentException("appName is required.", nameof(appName));
        if (string.IsNullOrWhiteSpace(logDirectory))
            throw new ArgumentException("logDirectory is required.", nameof(logDirectory));

        var levelSwitch = new LoggingLevelSwitch(
            debugMode ? LogEventLevel.Debug : LogEventLevel.Information);

        Logger logger;
        try
        {
            Directory.CreateDirectory(logDirectory);

            var path = Path.Combine(logDirectory, $"{appName.ToLowerInvariant()}-.ndjson");

            logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .Enrich.WithProperty("app", appName)
                .WriteTo.File(
                    formatter: new NdjsonFormatter(),
                    path: path,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileCountLimit,
                    fileSizeLimitBytes: FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    shared: false)
                .CreateLogger();
        }
        catch
        {
            // §4: a logging-setup failure must never disrupt the app. Fall back to a sink-less logger
            // (writes go nowhere) so the rest of the gadget runs normally.
            logger = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch).CreateLogger();
        }

        return new SerilogAppLogger(logger, includeStackTrace: debugMode);
    }
}
