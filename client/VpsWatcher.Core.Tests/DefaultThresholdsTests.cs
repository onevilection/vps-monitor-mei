using System.Collections.Generic;
using System.Linq;
using VpsWatcher.Core.Alerts;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Schema;

namespace VpsWatcher.Core.Tests;

/// <summary>
/// Phase 6b-fix §1: config thresholds are resolved to a fully-populated, validated set before the
/// state machine sees them — unset / invalid metrics fall back to the §6.2 defaults (each metric
/// independently), so a servers.json without a thresholds block still alerts.
/// </summary>
public class DefaultThresholdsTests
{
    [Fact]
    public void Null_thresholds_resolve_to_all_defaults()
    {
        var r = DefaultThresholds.Resolve(null, "vps-1");

        Assert.Equal(DefaultThresholds.Cpu, r.Cpu);
        Assert.Equal(DefaultThresholds.Mem, r.Mem);
        Assert.Equal(DefaultThresholds.Disk, r.Disk);
        Assert.Equal(DefaultThresholds.Swap, r.Swap);
    }

    [Fact]
    public void Only_missing_metrics_fall_back_the_rest_are_kept()
    {
        var configured = new ServerThresholds { Cpu = new double[] { 50, 60, 70 } }; // others null
        var r = DefaultThresholds.Resolve(configured, "vps-1");

        Assert.Equal(new double[] { 50, 60, 70 }, r.Cpu);  // kept
        Assert.Equal(DefaultThresholds.Mem, r.Mem);        // defaulted
        Assert.Equal(DefaultThresholds.Disk, r.Disk);      // defaulted
        Assert.Equal(DefaultThresholds.Swap, r.Swap);      // defaulted
    }

    [Theory]
    [InlineData(new double[] { 70, 85 })]            // too few
    [InlineData(new double[] { 70, 85, 95, 99 })]    // too many
    [InlineData(new double[] { 95, 85, 70 })]        // descending
    [InlineData(new double[] { 70, 70, 95 })]        // not strictly rising
    [InlineData(new double[] { -1, 85, 95 })]        // negative
    public void Invalid_metric_falls_back_to_default(double[] bad)
    {
        var r = DefaultThresholds.Resolve(new ServerThresholds { Cpu = bad }, "vps-1");
        Assert.Equal(DefaultThresholds.Cpu, r.Cpu);
    }

    [Fact]
    public void Valid_custom_thresholds_are_preserved()
    {
        var r = DefaultThresholds.Resolve(
            new ServerThresholds { Disk = new double[] { 60, 70, 80 } }, "vps-1");
        Assert.Equal(new double[] { 60, 70, 80 }, r.Disk);
    }

    [Theory]
    [InlineData(new double[] { 70, 85, 95 }, true)]
    [InlineData(new double[] { 0, 50, 100 }, true)]
    [InlineData(new double[] { 70, 85 }, false)]
    [InlineData(new double[] { 95, 85, 70 }, false)]
    public void IsValid_matches_the_contract(double[] t, bool expected)
        => Assert.Equal(expected, DefaultThresholds.IsValid(t));

    [Fact]
    public void Resolved_defaults_actually_drive_the_machine_to_alert()
    {
        // The whole point of the fix: no thresholds in config ⇒ resolve to defaults ⇒ a high disk
        // value still escalates (disk default warning entry = 90, so 94 ⇒ Warning after debounce).
        var m = new AlertStateMachine("vps-1", DefaultThresholds.Resolve(null, "vps-1"));
        for (int i = 0; i < 3; i++)
            m.ProcessSample(new Sample
            {
                Id = "vps-1",
                CpuPct = 1,
                Mem = new Mem { UsedPct = 1 },
                Swap = new Swap { UsedPct = 0 },
                Disk = new[] { new DiskEntry { Mount = "/", UsedPct = 94 } },
            });

        Assert.Equal(AlertLevel.Warning, m.State);
    }
}
