using System.Text.Json.Serialization;

namespace VpsWatcher.Core.Configuration;

/// <summary>
/// One server entry from <c>servers.json</c> (design §9.1). Real values (IP, key path,
/// host key) live only in the user's <c>%APPDATA%\VpsWatcher\servers.json</c> — never in
/// the repo. The repo ships <c>servers.example.json</c> with dummy values only.
/// </summary>
public sealed record ServerConfig
{
    /// <summary>Stable server id, matches NDJSON <c>id</c> (required).</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = "";

    /// <summary>Display label for the panel; falls back to <see cref="Id"/> when null.</summary>
    [JsonPropertyName("label")] public string? Label { get; init; }

    /// <summary>SSH host / IP (required).</summary>
    [JsonPropertyName("host")] public string Host { get; init; } = "";

    /// <summary>SSH port (required in practice; design uses 49222).</summary>
    [JsonPropertyName("port")] public int Port { get; init; }

    /// <summary>SSH user, e.g. <c>metrics</c> (required).</summary>
    [JsonPropertyName("user")] public string User { get; init; } = "";

    /// <summary>Path to the private key, empty passphrase (required).</summary>
    [JsonPropertyName("keyPath")] public string KeyPath { get; init; } = "";

    /// <summary>
    /// Pinned host-key fingerprint in <c>SHA256:base64-no-padding</c> form — the exact
    /// string from <c>ssh-keygen -lf</c> / the first-connect prompt (design §5.4.1/§9.1).
    /// REQUIRED: an empty value fails closed (we refuse to connect), because without a pin
    /// MITM detection cannot hold.
    /// </summary>
    [JsonPropertyName("knownHostKey")] public string KnownHostKey { get; init; } = "";

    /// <summary>Agent NIC hint (server-side concern; client does not need it).</summary>
    [JsonPropertyName("iface")] public string? Iface { get; init; }

    /// <summary>Agent mount list (server-side concern; client does not need it).</summary>
    [JsonPropertyName("mounts")] public IReadOnlyList<string>? Mounts { get; init; }

    /// <summary>Per-server thresholds (used by the alert state machine in a later phase; held only for now).</summary>
    [JsonPropertyName("thresholds")] public ServerThresholds? Thresholds { get; init; }

    /// <summary>
    /// Validates required fields. Throws <see cref="ArgumentException"/> on the first
    /// problem. Fail-closed on a missing <see cref="KnownHostKey"/> (§5.4.1/§9.1).
    /// </summary>
    public void Validate()
    {
        Require(Id, "id");
        Require(Host, "host");
        if (Port <= 0)
            throw new ArgumentException($"server '{Id}': port must be a positive number.");
        Require(User, "user");
        Require(KeyPath, "keyPath");
        // Fail-closed: a server without a pinned knownHostKey must not be connected to.
        if (string.IsNullOrWhiteSpace(KnownHostKey))
            throw new ArgumentException(
                $"server '{Id}': knownHostKey is required (MITM detection fails closed — " +
                "set the SHA256:... fingerprint from a trusted channel, design §5.4.1/§9.1).");
    }

    private void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"server '{Id}': {field} is required.");
    }
}

/// <summary>Per-server alert thresholds (design §6.2/§9.1). Three rising levels [caution, warning, critical].</summary>
public sealed record ServerThresholds
{
    [JsonPropertyName("cpu")] public IReadOnlyList<double>? Cpu { get; init; }
    [JsonPropertyName("mem")] public IReadOnlyList<double>? Mem { get; init; }
    [JsonPropertyName("disk")] public IReadOnlyList<double>? Disk { get; init; }
    [JsonPropertyName("swap")] public IReadOnlyList<double>? Swap { get; init; }
}
