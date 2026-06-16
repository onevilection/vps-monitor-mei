using System.Text.Json;
using VpsWatcher.Core.Schema;

namespace VpsWatcher.Core;

/// <summary>
/// Parses a single NDJSON line (schema §1) into a <see cref="Sample"/>.
/// Contract rules honoured here (§4): unknown fields are ignored (forward-compat),
/// known field types are strict, and a malformed line is dropped (<see cref="TryParse"/>)
/// rather than crashing the stream.
/// </summary>
public static class NdjsonParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // §4: case-sensitive, exact field names (our [JsonPropertyName] mirrors the contract).
        PropertyNameCaseInsensitive = false,
        // §4: unknown/extra fields are ignored — this is System.Text.Json's default, set
        // explicitly so a future strict-mode flip elsewhere can't silently break compat.
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>
    /// Parses one NDJSON line into a <see cref="Sample"/>. Throws <see cref="JsonException"/>
    /// on malformed input. Use <see cref="TryParse"/> for stream resilience.
    /// </summary>
    public static Sample Parse(string line)
    {
        var sample = JsonSerializer.Deserialize<Sample>(line, Options);
        if (sample is null)
            throw new JsonException("NDJSON line deserialized to null.");
        return sample;
    }

    /// <summary>
    /// Tries to parse one NDJSON line. Returns <c>false</c> (and a null sample) for a
    /// malformed line so the caller can drop it and keep reading the stream (§4).
    /// </summary>
    public static bool TryParse(string line, out Sample? sample)
    {
        try
        {
            sample = Parse(line);
            return true;
        }
        catch (JsonException)
        {
            sample = null;
            return false;
        }
    }
}
