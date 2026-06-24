namespace VpsWatcher.App.Configuration;

/// <summary>
/// Reads process-level startup options from the command line (instruction §5). Kept separate from
/// <see cref="AppServerConfigLoader"/> (which resolves the connection target) so the debug switch can
/// be parsed and unit-tested on its own and coexists with the existing connection args.
/// </summary>
public static class LaunchOptions
{
    /// <summary>
    /// True when <c>--debug_mode</c> appears in the arguments (case-insensitive). debug_mode ON lowers
    /// the log threshold to Debug+; absent, it stays at Info+ (§5).
    /// </summary>
    public static bool IsDebugMode(string[] args)
        => args.Any(a => string.Equals(a, "--debug_mode", StringComparison.OrdinalIgnoreCase));
}
