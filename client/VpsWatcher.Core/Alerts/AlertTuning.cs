namespace VpsWatcher.Core.Alerts;

/// <summary>
/// The three anti-flap tuning constants (design §6.3/§6.4), gathered in one place so they can later
/// be made configurable. Percentages are in percentage-points (all judged metrics are 0..100 %).
/// </summary>
internal static class AlertTuning
{
    /// <summary>Exit threshold = entry − this margin (hysteresis, §6.3). 警告へ入るのは入口超、出るのは入口−5。</summary>
    public const double HysteresisMarginPct = 5.0;

    /// <summary>Consecutive over-threshold samples required to promote (debounce, §6.3). 降格は要求しない。</summary>
    public const int DebounceSamples = 3;

    /// <summary>Per-server, per-level voice cooldown (§6.4). 上位昇格は貫通・同レベル以下は抑制。</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
}
