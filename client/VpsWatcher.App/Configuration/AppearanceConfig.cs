using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpsWatcher.Core.Alerts;
using VpsWatcher.Core.Logging;

namespace VpsWatcher.App.Configuration;

/// <summary>
/// The character's expression as a function of the overall (worst-of) state (design §8). Mirrors
/// <see cref="AlertLevel"/> plus the transient <see cref="Recovery"/> shown the moment the worst
/// state drops back to Normal. These names are the keys in <c>appearance.json</c>'s
/// <c>expressions</c> map, so they are part of the user-facing contract.
/// </summary>
public enum CharacterMood
{
    Normal,
    Caution,
    Warning,
    Critical,
    Disconnected,
    HostKeyMismatch,
    Recovery,
}

/// <summary>
/// User-editable look settings persisted to <c>%APPDATA%\VpsWatcher\appearance.json</c> (design §8 /
/// §9.2). Holds the state→PNG expression map and the panel background opacity. Kept WPF-free (pure
/// POCO + pure resolution) so it is unit-testable without a Dispatcher; the WPF glue (decoding /
/// freezing the images, building the brush) lives in the View layer.
///
/// Forward-compat: voice mapping (§7, Phase 6c) will land as another key on this same file, so
/// unknown keys are round-tripped via <see cref="Extra"/> rather than dropped.
/// </summary>
public sealed class AppearanceConfig
{
    /// <summary>Panel background opacity 0..1 (1 = opaque). Absent / invalid → <see cref="DefaultOpacity"/>.</summary>
    [JsonPropertyName("background_opacity")]
    public double? BackgroundOpacity { get; set; }

    /// <summary>Mood name (<see cref="CharacterMood"/>) → PNG file name. Missing entries fall back to
    /// the bundled default for that mood.</summary>
    [JsonPropertyName("expressions")]
    public Dictionary<string, string>? Expressions { get; set; }

    /// <summary>Round-trips keys this phase doesn't model yet (e.g. 6c voice map) so they survive a save.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Default panel opacity (matches the 6a hard-coded <c>#E6…</c> = 230/255 ≈ 0.9).</summary>
    public const double DefaultOpacity = 0.9;

    /// <summary>Bundled default expression PNGs per mood (§8). The file names match the repo's
    /// <c>assets/char/</c> assets compiled into the app's resources.</summary>
    public static readonly IReadOnlyDictionary<CharacterMood, string> DefaultExpressions =
        new Dictionary<CharacterMood, string>
        {
            [CharacterMood.Normal] = "mei-hutuu.png",
            [CharacterMood.Caution] = "mei-nayami.png",
            [CharacterMood.Warning] = "mei-odoroki.png",
            [CharacterMood.Critical] = "mei-obie.png",
            [CharacterMood.Disconnected] = "mei-konnrann.png",
            [CharacterMood.HostKeyMismatch] = "mei-ikari.png",
            [CharacterMood.Recovery] = "mei-yorokobi.png",
        };

    /// <summary>Maps the overall alert level to its mood (Recovery is driven separately, on the
    /// non-Normal → Normal transition).</summary>
    public static CharacterMood MoodFor(AlertLevel level) => level switch
    {
        AlertLevel.Caution => CharacterMood.Caution,
        AlertLevel.Warning => CharacterMood.Warning,
        AlertLevel.Critical => CharacterMood.Critical,
        AlertLevel.Disconnected => CharacterMood.Disconnected,
        AlertLevel.HostKeyMismatch => CharacterMood.HostKeyMismatch,
        _ => CharacterMood.Normal,
    };

    /// <summary>Effective opacity in [0,1]: out-of-range values are clamped; null / NaN fall back to
    /// <see cref="DefaultOpacity"/> (Phase 6b §5).</summary>
    public double EffectiveOpacity()
    {
        if (BackgroundOpacity is not { } v || double.IsNaN(v))
            return DefaultOpacity;
        return Math.Clamp(v, 0.0, 1.0);
    }

    /// <summary>Effective opacity as an 8-bit alpha for the ARGB background colour.</summary>
    public byte EffectiveAlphaByte() => (byte)Math.Round(EffectiveOpacity() * 255.0);

    /// <summary>The configured (or default) PNG file name for a mood. Blank / missing → bundled default.</summary>
    public string FileNameFor(CharacterMood mood)
    {
        if (Expressions is not null
            && Expressions.TryGetValue(mood.ToString(), out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }
        return DefaultExpressions[mood];
    }
}

/// <summary>
/// Load/save for <see cref="AppearanceConfig"/>. Fail-soft on read (missing / corrupt → defaults,
/// never throws) so a bad appearance.json can't stop the gadget from launching (Phase 6b §4).
/// </summary>
public static class AppearanceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Default path: <c>%APPDATA%\VpsWatcher\appearance.json</c> (outside the repo).</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VpsWatcher", "appearance.json");

    /// <summary>User PNG override directory: <c>%APPDATA%\VpsWatcher\assets\char</c> — a PNG dropped
    /// here (matching a configured/default file name) replaces the bundled one (§8 user-overridable).</summary>
    public static string DefaultUserCharDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VpsWatcher", "assets", "char");

    public static AppearanceConfig Load(string path, IAppLogger? logger = null)
    {
        try
        {
            if (!File.Exists(path))
                return new AppearanceConfig();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppearanceConfig>(json, Options) ?? new AppearanceConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fail-soft: fall back to bundled defaults. Reason only — no secrets / paths (§4).
            logger?.Log(LogSeverity.Warning, "appearance.json unreadable; using defaults",
                new Dictionary<string, object?> { ["reason"] = ex.GetType().Name });
            return new AppearanceConfig();
        }
    }
}
