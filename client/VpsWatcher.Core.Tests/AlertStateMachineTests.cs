using System;
using System.Collections.Generic;
using System.Linq;
using VpsWatcher.Core.Alerts;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Schema;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.Core.Tests;

/// <summary>
/// Pure-logic tests for the alert state machine (design §6 / 付録B). No network, no UI. The clock is
/// injected so cooldown is deterministic. Promotion is strict-greater-than the entry; demotion is
/// strict-less-than (entry − 5pt margin); promotion needs 3 consecutive samples; demotion is immediate.
/// </summary>
public class AlertStateMachineTests
{
    // Design §6.2 defaults. CPU entry: caution 70 / warning 85 / critical 95.
    private static ServerThresholds StdThresholds() => new()
    {
        Cpu = new double[] { 70, 85, 95 },
        Mem = new double[] { 75, 88, 95 },
        Disk = new double[] { 80, 90, 95 },
        Swap = new double[] { 25, 50, 80 },
    };

    /// <summary>A sample where only CPU varies; mem/swap/disk are held well below caution so CPU
    /// alone drives the overall state.</summary>
    private static Sample CpuSample(double? cpu) => new()
    {
        Id = "vps-1",
        CpuPct = cpu,
        Mem = new Mem { UsedPct = 1 },
        Swap = new Swap { UsedPct = 0 },
        Disk = new[] { new DiskEntry { Mount = "/", UsedPct = 1 } },
    };

    private static Sample DiskSample(params (string mount, double usedPct)[] disks) => new()
    {
        Id = "vps-1",
        CpuPct = 1,
        Mem = new Mem { UsedPct = 1 },
        Swap = new Swap { UsedPct = 0 },
        Disk = disks.Select(d => new DiskEntry { Mount = d.mount, UsedPct = d.usedPct }).ToArray(),
    };

    private sealed class TestClock
    {
        public DateTime Now = new(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
        public Func<DateTime> Func => () => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static AlertStateMachine Machine(out List<AlertStateChangedEventArgs> changes,
        out List<AlertTriggeredEventArgs> triggers, ServerThresholds? thresholds = null,
        Func<DateTime>? clock = null)
    {
        var m = new AlertStateMachine("vps-1", thresholds ?? StdThresholds(), logger: null, clock: clock);
        var c = new List<AlertStateChangedEventArgs>();
        var t = new List<AlertTriggeredEventArgs>();
        m.StateChanged += (_, e) => c.Add(e);
        m.AlertTriggered += (_, e) => t.Add(e);
        changes = c;
        triggers = t;
        return m;
    }

    private static void Feed(AlertStateMachine m, double? cpu, int times)
    {
        for (int i = 0; i < times; i++)
            m.ProcessSample(CpuSample(cpu));
    }

    // ───────────────────────── promotion / debounce ─────────────────────────

    [Fact]
    public void Starts_normal()
    {
        var m = Machine(out _, out _);
        Assert.Equal(AlertLevel.Normal, m.State);
    }

    [Fact]
    public void A_single_over_threshold_sample_does_not_promote()
    {
        var m = Machine(out _, out _);
        m.ProcessSample(CpuSample(86)); // > warning entry 85, but only 1 sample
        Assert.Equal(AlertLevel.Normal, m.State);
    }

    [Fact]
    public void Three_consecutive_over_threshold_samples_promote()
    {
        var m = Machine(out _, out _);
        Feed(m, 86, 2);
        Assert.Equal(AlertLevel.Normal, m.State); // still debouncing after 2
        m.ProcessSample(CpuSample(86));
        Assert.Equal(AlertLevel.Warning, m.State); // 3rd consecutive ⇒ promote
    }

    [Fact]
    public void A_dip_resets_the_debounce_counter()
    {
        var m = Machine(out _, out _);
        Feed(m, 86, 2);
        m.ProcessSample(CpuSample(50)); // dip ⇒ resets counter
        Feed(m, 86, 2);
        Assert.Equal(AlertLevel.Normal, m.State); // only 2 since the reset
        m.ProcessSample(CpuSample(86));
        Assert.Equal(AlertLevel.Warning, m.State); // now 3 since reset
    }

    [Fact]
    public void Promotion_to_normal_through_critical_lands_on_the_classified_level()
    {
        var m = Machine(out _, out _);
        Feed(m, 96, 3); // > critical entry 95
        Assert.Equal(AlertLevel.Critical, m.State); // jumps straight to Critical (worst-of band)
    }

    // ───────────────────────── boundaries (strictness) ─────────────────────────

    [Fact]
    public void Exactly_the_entry_value_does_not_promote_into_that_level()
    {
        var m = Machine(out _, out _);
        Feed(m, 85, 3); // == warning entry ⇒ NOT warning (strict >). 85 > caution 70 ⇒ Caution.
        Assert.Equal(AlertLevel.Caution, m.State);
    }

    [Fact]
    public void Exactly_the_exit_value_holds_the_current_level()
    {
        var m = Machine(out _, out _);
        Feed(m, 86, 3);
        Assert.Equal(AlertLevel.Warning, m.State);

        m.ProcessSample(CpuSample(80)); // == exit (85 − 5); strict-less demote ⇒ holds Warning
        Assert.Equal(AlertLevel.Warning, m.State);
    }

    // ───────────────────────── hysteresis ─────────────────────────

    [Fact]
    public void Stays_in_warning_between_exit_and_next_entry()
    {
        var m = Machine(out _, out _);
        Feed(m, 86, 3);
        m.ProcessSample(CpuSample(83)); // below entry but above exit 80
        Assert.Equal(AlertLevel.Warning, m.State);
    }

    [Fact]
    public void Demotes_below_the_exit_threshold()
    {
        var m = Machine(out _, out _);
        Feed(m, 86, 3);
        m.ProcessSample(CpuSample(79)); // < exit 80 ⇒ demote to Caution (still > caution entry 70)
        Assert.Equal(AlertLevel.Caution, m.State);
    }

    [Fact]
    public void Vibration_around_the_entry_does_not_chatter()
    {
        var m = Machine(out var changes, out _);
        Feed(m, 86, 3); // enter Warning (1 change)
        for (int i = 0; i < 10; i++)
            m.ProcessSample(CpuSample(i % 2 == 0 ? 84 : 86)); // 84↔86, never < 80, never > 95

        Assert.Equal(AlertLevel.Warning, m.State);
        Assert.Single(changes); // only the initial entry into Warning
    }

    [Fact]
    public void Demotion_is_immediate_no_debounce()
    {
        var m = Machine(out _, out _);
        Feed(m, 86, 3);
        m.ProcessSample(CpuSample(10)); // one sample well below all exits
        Assert.Equal(AlertLevel.Normal, m.State);
    }

    // ───────────────────────── null (measuring) ─────────────────────────

    [Fact]
    public void Null_cpu_sample_does_not_change_the_cpu_level()
    {
        var m = Machine(out _, out _);
        Feed(m, 86, 3);
        Assert.Equal(AlertLevel.Warning, m.State);

        m.ProcessSample(CpuSample(null)); // measuring ⇒ skip; must NOT be treated as 0 ⇒ no demote
        Assert.Equal(AlertLevel.Warning, m.State);
    }

    [Fact]
    public void Null_cpu_does_not_promote_from_normal()
    {
        var m = Machine(out _, out _);
        for (int i = 0; i < 5; i++)
            m.ProcessSample(CpuSample(null));
        Assert.Equal(AlertLevel.Normal, m.State);
    }

    // ───────────────────────── worst-of ─────────────────────────

    [Fact]
    public void Worst_of_picks_the_highest_metric()
    {
        var m = Machine(out var changes, out _);
        for (int i = 0; i < 3; i++)
            m.ProcessSample(DiskSample(("/", 96))); // disk critical, cpu/mem/swap normal
        Assert.Equal(AlertLevel.Critical, m.State);
        Assert.Contains("disk", changes.Last().Cause);
    }

    [Fact]
    public void Worst_disk_among_several_mounts_wins()
    {
        var m = Machine(out _, out _);
        for (int i = 0; i < 3; i++)
            m.ProcessSample(DiskSample(("/", 10), ("/data", 96)));
        Assert.Equal(AlertLevel.Critical, m.State); // /data critical drives it
    }

    // ───────────────────────── connection priority ─────────────────────────

    [Fact]
    public void HostKeyMismatch_overrides_any_metric_state()
    {
        var m = Machine(out _, out _);
        Feed(m, 96, 3);
        Assert.Equal(AlertLevel.Critical, m.State);

        m.ProcessConnectionState(ConnectionState.HostKeyMismatch);
        Assert.Equal(AlertLevel.HostKeyMismatch, m.State);
    }

    [Fact]
    public void Disconnected_outranks_metric_critical_but_not_hostkeymismatch()
    {
        var m = Machine(out _, out _);
        Feed(m, 96, 3); // Critical
        m.ProcessConnectionState(ConnectionState.Disconnected);
        Assert.Equal(AlertLevel.Disconnected, m.State);
    }

    [Fact]
    public void Reconnect_then_a_normal_sample_recovers_to_normal()
    {
        var m = Machine(out _, out _);
        Feed(m, 96, 3);
        m.ProcessConnectionState(ConnectionState.Disconnected);
        Assert.Equal(AlertLevel.Disconnected, m.State);

        m.ProcessConnectionState(ConnectionState.Connected);
        m.ProcessSample(CpuSample(10)); // immediate demote (no debounce on the way down)
        Assert.Equal(AlertLevel.Normal, m.State);
    }

    // ───────────────────────── StateChanged firing ─────────────────────────

    [Fact]
    public void StateChanged_fires_only_on_change()
    {
        var m = Machine(out var changes, out _);
        Feed(m, 10, 5); // all normal
        Assert.Empty(changes);

        Feed(m, 86, 3); // one promotion to Warning
        Assert.Single(changes);
        Assert.Equal(AlertLevel.Normal, changes[0].OldState);
        Assert.Equal(AlertLevel.Warning, changes[0].NewState);
    }

    // ───────────────────────── cooldown / AlertTriggered ─────────────────────────

    [Fact]
    public void Escalation_raises_alert_triggered_demotion_does_not()
    {
        var m = Machine(out _, out var triggers);
        Feed(m, 86, 3); // ⇒ Warning
        Assert.Single(triggers);
        Assert.Equal(AlertLevel.Warning, triggers[0].Level);

        m.ProcessSample(CpuSample(10)); // demote ⇒ no trigger
        Assert.Single(triggers);
    }

    [Fact]
    public void Same_level_re_alert_within_cooldown_is_suppressed()
    {
        var clock = new TestClock();
        var m = Machine(out _, out var triggers, clock: clock.Func);

        Feed(m, 86, 3); // Warning fires at t0
        Assert.Single(triggers);

        m.ProcessSample(CpuSample(10)); // demote to Normal
        clock.Advance(TimeSpan.FromMinutes(2)); // < 5 min
        Feed(m, 86, 3); // back to Warning — suppressed
        Assert.Single(triggers);
    }

    [Fact]
    public void Same_level_re_alert_after_cooldown_fires_again()
    {
        var clock = new TestClock();
        var m = Machine(out _, out var triggers, clock: clock.Func);

        Feed(m, 86, 3);
        m.ProcessSample(CpuSample(10));
        clock.Advance(TimeSpan.FromMinutes(6)); // > 5 min
        Feed(m, 86, 3);
        Assert.Equal(2, triggers.Count);
    }

    [Fact]
    public void Escalation_to_a_higher_level_pierces_the_cooldown()
    {
        var clock = new TestClock();
        var m = Machine(out _, out var triggers, clock: clock.Func);

        Feed(m, 86, 3); // Warning at t0
        Assert.Single(triggers);

        clock.Advance(TimeSpan.FromMinutes(1)); // still within Warning's cooldown
        Feed(m, 96, 3); // escalate to Critical ⇒ pierces, fires immediately
        Assert.Equal(2, triggers.Count);
        Assert.Equal(AlertLevel.Critical, triggers[1].Level);
    }

    // ───────────────────────── thresholds absent ─────────────────────────

    [Fact]
    public void Metric_without_thresholds_is_never_alerted()
    {
        // No thresholds at all ⇒ every metric skipped ⇒ stays Normal regardless of values.
        var m = Machine(out _, out _, thresholds: new ServerThresholds());
        for (int i = 0; i < 5; i++)
            m.ProcessSample(CpuSample(99));
        Assert.Equal(AlertLevel.Normal, m.State);
    }
}
