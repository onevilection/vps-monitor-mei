using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Logging;

namespace VpsWatcher.Core.Alerts;

/// <summary>
/// The design §6.2 default alert thresholds and the resolver that fills them in when a server's
/// <c>servers.json</c> omits / mis-specifies them.
///
/// Why this exists (bug fix): the <see cref="AlertStateMachine"/> skips judging a metric that has no
/// (valid) thresholds — so a servers.json without a <c>thresholds</c> block left every metric stuck
/// at Normal, and alerts/expressions never fired no matter how high the values went. Rather than
/// change the machine's contract (a metric with no thresholds still means "don't judge", Phase 5
/// test維持), we resolve config thresholds to a fully-populated, validated set BEFORE handing them to
/// the machine: unset / invalid metrics fall back to these defaults, each metric independently.
/// </summary>
public static class DefaultThresholds
{
    // [caution, warning, critical] entry values, in percentage points (design §6.2).
    public static readonly IReadOnlyList<double> Cpu = new double[] { 70, 85, 95 };
    public static readonly IReadOnlyList<double> Mem = new double[] { 75, 88, 95 };
    public static readonly IReadOnlyList<double> Disk = new double[] { 80, 90, 95 };
    public static readonly IReadOnlyList<double> Swap = new double[] { 25, 50, 80 };

    /// <summary>
    /// Returns a thresholds set where every metric is present and valid: each of cpu/mem/disk/swap is
    /// taken from <paramref name="configured"/> when it is a valid rising triple, otherwise replaced
    /// with the §6.2 default. Logs (secret-free: server id + which metrics defaulted and why) when any
    /// fallback happened. Never throws — fail-soft.
    /// </summary>
    public static ServerThresholds Resolve(
        ServerThresholds? configured, string serverId, IAppLogger? logger = null)
    {
        var notes = new List<string>();
        var resolved = new ServerThresholds
        {
            Cpu = ResolveMetric(configured?.Cpu, Cpu, "cpu", notes),
            Mem = ResolveMetric(configured?.Mem, Mem, "mem", notes),
            Disk = ResolveMetric(configured?.Disk, Disk, "disk", notes),
            Swap = ResolveMetric(configured?.Swap, Swap, "swap", notes),
        };

        if (notes.Count > 0)
            logger?.Log(LogSeverity.Info, "applied default thresholds", new Dictionary<string, object?>
            {
                ["server_id"] = serverId,
                ["metrics"] = string.Join(",", notes), // e.g. "mem:missing,disk:invalid" (no secrets)
            });

        return resolved;
    }

    private static IReadOnlyList<double> ResolveMetric(
        IReadOnlyList<double>? value, IReadOnlyList<double> fallback, string name, List<string> notes)
    {
        if (value is null)
        {
            notes.Add(name + ":missing");
            return fallback;
        }
        if (!IsValid(value))
        {
            notes.Add(name + ":invalid");
            return fallback;
        }
        return value;
    }

    /// <summary>A valid threshold triple: exactly 3 finite, non-negative, strictly-rising entries
    /// (caution &lt; warning &lt; critical). Anything else (wrong count, descending, negative, NaN) is
    /// rejected so it falls back to the default.</summary>
    public static bool IsValid(IReadOnlyList<double>? t)
    {
        if (t is null || t.Count != 3)
            return false;
        foreach (var v in t)
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
                return false;
        return t[0] < t[1] && t[1] < t[2];
    }
}
