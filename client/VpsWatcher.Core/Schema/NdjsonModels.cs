using System.Text.Json.Serialization;

namespace VpsWatcher.Core.Schema;

// Strongly-typed mirror of the NDJSON contract.
// SINGLE SOURCE OF TRUTH: docs/ndjson-schema.md + testdata/*.ndjson.
// Do not change shape/nullability here without updating the contract first
// (contract -> both-sides tests -> implementation). See CLAUDE.md.

/// <summary>
/// One NDJSON sample = one line = one server snapshot (schema §2).
/// </summary>
public sealed record Sample
{
    /// <summary>Schema version (§2). Currently 1; only bumped on breaking changes.</summary>
    [JsonPropertyName("v")] public int V { get; init; }

    /// <summary>Server identifier (§2). Matches servers.json `id`. Never null.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = "";

    /// <summary>Server-stamped sample time, Unix epoch seconds UTC (§2/§7). History axis is keyed on this.</summary>
    [JsonPropertyName("ts")] public long Ts { get; init; }

    /// <summary>
    /// CPU usage % 0-100. Rate field: <c>null</c> means "measuring / unknown" (§3),
    /// which is NOT the same as 0. Never substitute a value for null.
    /// </summary>
    [JsonPropertyName("cpu_pct")] public double? CpuPct { get; init; }

    [JsonPropertyName("mem")] public Mem Mem { get; init; } = new();

    [JsonPropertyName("swap")] public Swap Swap { get; init; } = new();

    /// <summary>Per-mount storage (§2.3). 0..N entries; never assume a fixed count (§4).</summary>
    [JsonPropertyName("disk")] public IReadOnlyList<DiskEntry> Disk { get; init; } = Array.Empty<DiskEntry>();

    [JsonPropertyName("net")] public Net Net { get; init; } = new();

    /// <summary>Load average [1m, 5m, 15m] (§2). Always 3 elements.</summary>
    [JsonPropertyName("load")] public IReadOnlyList<double> Load { get; init; } = Array.Empty<double>();

    /// <summary>Uptime seconds, from /proc/uptime (§2).</summary>
    [JsonPropertyName("uptime_sec")] public long UptimeSec { get; init; }
}

/// <summary>Memory (§2.1). All fields always present.</summary>
public sealed record Mem
{
    [JsonPropertyName("used_pct")] public double UsedPct { get; init; }
    [JsonPropertyName("used_mb")] public long UsedMb { get; init; }
    [JsonPropertyName("total_mb")] public long TotalMb { get; init; }
}

/// <summary>Swap (§2.2). 0.0 when SwapTotal is 0.</summary>
public sealed record Swap
{
    [JsonPropertyName("used_pct")] public double UsedPct { get; init; }
}

/// <summary>One mount point's storage usage (§2.3).</summary>
public sealed record DiskEntry
{
    [JsonPropertyName("mount")] public string Mount { get; init; } = "";

    /// <summary>Physical used % (f_bfree based) — used for display/threshold (§2.3).</summary>
    [JsonPropertyName("used_pct")] public double UsedPct { get; init; }

    /// <summary>User-visible used GiB (f_bavail based) (§2.3).</summary>
    [JsonPropertyName("used_gb")] public double UsedGb { get; init; }

    [JsonPropertyName("total_gb")] public double TotalGb { get; init; }
}

/// <summary>Network (§2.4).</summary>
public sealed record Net
{
    [JsonPropertyName("iface")] public string Iface { get; init; } = "";

    /// <summary>Receive rate bytes/sec. Rate field: <c>null</c> = measuring/unknown (§3), not 0.</summary>
    [JsonPropertyName("rx_bps")] public double? RxBps { get; init; }

    /// <summary>Transmit rate bytes/sec. Rate field: <c>null</c> = measuring/unknown (§3), not 0.</summary>
    [JsonPropertyName("tx_bps")] public double? TxBps { get; init; }
}
