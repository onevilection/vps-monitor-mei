using System.IO;
using VpsWatcher.App.Configuration;
using VpsWatcher.Core.Alerts;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Phase 6b §4/§5: the appearance config resolves the state→PNG expression map and the background
/// opacity, with fail-soft defaults. Pure (no WPF), so exercised directly.
/// </summary>
public sealed class AppearanceConfigTests
{
    // ───────────────────────── opacity (§5) ─────────────────────────

    [Fact]
    public void Opacity_defaults_to_0_9_when_absent()
    {
        var cfg = new AppearanceConfig(); // background_opacity null
        Assert.Equal(0.9, cfg.EffectiveOpacity(), 3);
        Assert.Equal((byte)230, cfg.EffectiveAlphaByte()); // 0.9*255 = 229.5 → 230 (≈ the 6a #E6)
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.5, 1.0)]   // > 1 clamps
    [InlineData(-0.3, 0.0)]  // < 0 clamps
    public void Opacity_clamps_to_unit_range(double input, double expected)
    {
        var cfg = new AppearanceConfig { BackgroundOpacity = input };
        Assert.Equal(expected, cfg.EffectiveOpacity(), 3);
    }

    [Fact]
    public void Opacity_nan_falls_back_to_default()
    {
        var cfg = new AppearanceConfig { BackgroundOpacity = double.NaN };
        Assert.Equal(0.9, cfg.EffectiveOpacity(), 3);
    }

    [Theory]
    [InlineData(0.0, (byte)0)]
    [InlineData(1.0, (byte)255)]
    [InlineData(0.5, (byte)128)] // 127.5 → 128 (round half away from zero)
    public void Alpha_byte_is_opacity_times_255(double opacity, byte expected)
    {
        var cfg = new AppearanceConfig { BackgroundOpacity = opacity };
        Assert.Equal(expected, cfg.EffectiveAlphaByte());
    }

    // ───────────────────────── low-opacity dark text (§3) ─────────────────────────

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.29, true)]
    [InlineData(0.3, false)]   // boundary: 0.3 exactly ⇒ still light text
    [InlineData(0.31, false)]
    [InlineData(0.9, false)]
    public void UseDarkText_only_below_the_threshold(double opacity, bool expectDark)
    {
        var cfg = new AppearanceConfig { BackgroundOpacity = opacity };
        Assert.Equal(expectDark, cfg.UseDarkText());
    }

    [Fact]
    public void UseDarkText_uses_the_default_opacity_when_unset()
        => Assert.False(new AppearanceConfig().UseDarkText()); // default 0.9 ⇒ light

    // ───────────────────────── expression map (§4) ─────────────────────────

    [Theory]
    [InlineData(AlertLevel.Normal, CharacterMood.Normal, "mei-hutuu.png")]
    [InlineData(AlertLevel.Caution, CharacterMood.Caution, "mei-nayami.png")]
    [InlineData(AlertLevel.Warning, CharacterMood.Warning, "mei-odoroki.png")]
    [InlineData(AlertLevel.Critical, CharacterMood.Critical, "mei-obie.png")]
    [InlineData(AlertLevel.Disconnected, CharacterMood.Disconnected, "mei-konnrann.png")]
    [InlineData(AlertLevel.HostKeyMismatch, CharacterMood.HostKeyMismatch, "mei-ikari.png")]
    public void Level_maps_to_mood_and_default_png(AlertLevel level, CharacterMood mood, string png)
    {
        Assert.Equal(mood, AppearanceConfig.MoodFor(level));
        Assert.Equal(png, new AppearanceConfig().FileNameFor(mood));
    }

    [Fact]
    public void Recovery_default_png_is_yorokobi()
        => Assert.Equal("mei-yorokobi.png", new AppearanceConfig().FileNameFor(CharacterMood.Recovery));

    [Fact]
    public void Configured_expression_overrides_the_default()
    {
        var cfg = new AppearanceConfig
        {
            Expressions = new() { ["Critical"] = "my-panic.png" },
        };
        Assert.Equal("my-panic.png", cfg.FileNameFor(CharacterMood.Critical));
        // Unspecified moods still fall back to bundled defaults.
        Assert.Equal("mei-hutuu.png", cfg.FileNameFor(CharacterMood.Normal));
    }

    [Fact]
    public void Blank_expression_value_falls_back_to_default()
    {
        var cfg = new AppearanceConfig { Expressions = new() { ["Normal"] = "   " } };
        Assert.Equal("mei-hutuu.png", cfg.FileNameFor(CharacterMood.Normal));
    }
}

/// <summary>Load tests for appearance.json — fail-soft on missing / corrupt files (Phase 6b §4).</summary>
public sealed class AppearanceStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"vpswatcher_appearance_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var cfg = AppearanceStore.Load(_path); // never created
        Assert.Equal(0.9, cfg.EffectiveOpacity(), 3);
        Assert.Equal("mei-hutuu.png", cfg.FileNameFor(CharacterMood.Normal));
    }

    [Fact]
    public void Load_corrupt_file_returns_defaults_without_throwing()
    {
        File.WriteAllText(_path, "{ not valid json ");
        var cfg = AppearanceStore.Load(_path);
        Assert.Equal(0.9, cfg.EffectiveOpacity(), 3);
        Assert.Equal("mei-obie.png", cfg.FileNameFor(CharacterMood.Critical));
    }

    [Fact]
    public void Load_reads_opacity_and_expression_overrides()
    {
        File.WriteAllText(_path,
            "{\"background_opacity\":0.4,\"expressions\":{\"Warning\":\"custom-warn.png\"}}");

        var cfg = AppearanceStore.Load(_path);

        Assert.Equal(0.4, cfg.EffectiveOpacity(), 3);
        Assert.Equal("custom-warn.png", cfg.FileNameFor(CharacterMood.Warning));
        Assert.Equal("mei-hutuu.png", cfg.FileNameFor(CharacterMood.Normal)); // unspecified → default
    }
}
