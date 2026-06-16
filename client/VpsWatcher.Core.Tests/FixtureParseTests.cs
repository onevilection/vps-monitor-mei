using VpsWatcher.Core;

namespace VpsWatcher.Core.Tests;

/// <summary>
/// The client half of the schema contract: these tests parse the SAME three canonical
/// fixtures the agent (Go) tests emit, guarding against schema/parser drift (schema §6).
/// </summary>
public class FixtureParseTests
{
    [Fact]
    public void Normal_sample_parses_all_fields_with_numeric_rates()
    {
        var sample = NdjsonParser.Parse(TestData.ReadFirstLine("sample.ndjson"));

        Assert.Equal(1, sample.V);
        Assert.Equal("vps-example-1", sample.Id);
        Assert.Equal(1717300000L, sample.Ts);

        // Rate fields present -> numeric (not null).
        Assert.Equal(12.4, sample.CpuPct);

        Assert.Equal(63.2, sample.Mem.UsedPct);
        Assert.Equal(2521L, sample.Mem.UsedMb);
        Assert.Equal(3989L, sample.Mem.TotalMb);

        Assert.Equal(0.0, sample.Swap.UsedPct);

        var disk = Assert.Single(sample.Disk);
        Assert.Equal("/", disk.Mount);
        Assert.Equal(48.1, disk.UsedPct);
        Assert.Equal(24.0, disk.UsedGb);
        Assert.Equal(50.0, disk.TotalGb);

        Assert.Equal("eth0", sample.Net.Iface);
        Assert.Equal(102400.0, sample.Net.RxBps);
        Assert.Equal(51200.0, sample.Net.TxBps);

        Assert.Equal(new[] { 0.12, 0.08, 0.05 }, sample.Load);
        Assert.Equal(864000L, sample.UptimeSec);
    }

    [Fact]
    public void Measuring_sample_keeps_rate_fields_null_not_zero()
    {
        var sample = NdjsonParser.Parse(TestData.ReadFirstLine("sample_measuring.ndjson"));

        // §3: null means "measuring / unknown" and must stay null (never 0).
        Assert.Null(sample.CpuPct);
        Assert.Null(sample.Net.RxBps);
        Assert.Null(sample.Net.TxBps);

        // Non-rate fields are always present even during the measuring frame.
        Assert.Equal(63.0, sample.Mem.UsedPct);
        Assert.Equal(0.0, sample.Swap.UsedPct);
        Assert.Equal("eth0", sample.Net.Iface);
    }

    [Fact]
    public void Multidisk_sample_reads_every_disk_entry_in_order()
    {
        var sample = NdjsonParser.Parse(TestData.ReadFirstLine("sample_multidisk.ndjson"));

        Assert.Equal(2, sample.Disk.Count);
        Assert.Equal("/", sample.Disk[0].Mount);
        Assert.Equal("/var/www", sample.Disk[1].Mount);
        Assert.Equal(67.3, sample.Disk[1].UsedPct);
        Assert.Equal(134.6, sample.Disk[1].UsedGb);
        Assert.Equal(200.0, sample.Disk[1].TotalGb);
    }

    [Fact]
    public void TryParse_drops_malformed_line_without_throwing()
    {
        // §4: a bad line must be dropped, not crash the stream.
        var ok = NdjsonParser.TryParse("{ this is not valid json", out var sample);

        Assert.False(ok);
        Assert.Null(sample);
    }

    [Fact]
    public void Unknown_fields_are_ignored_for_forward_compatibility()
    {
        // §4/§5: adding fields within the same `v` is a compatible change.
        const string line =
            "{\"v\":1,\"id\":\"x\",\"ts\":1,\"cpu_pct\":1.0," +
            "\"mem\":{\"used_pct\":1.0,\"used_mb\":1,\"total_mb\":2}," +
            "\"swap\":{\"used_pct\":0.0},\"disk\":[]," +
            "\"net\":{\"iface\":\"eth0\",\"rx_bps\":1.0,\"tx_bps\":1.0}," +
            "\"load\":[0.0,0.0,0.0],\"uptime_sec\":1,\"future_field\":123}";

        var sample = NdjsonParser.Parse(line);

        Assert.Equal("x", sample.Id);
        Assert.Empty(sample.Disk);
    }
}
