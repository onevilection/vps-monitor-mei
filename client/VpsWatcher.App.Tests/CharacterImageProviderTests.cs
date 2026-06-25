using System.IO;
using VpsWatcher.App.Configuration;
using VpsWatcher.App.Services;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Phase 6b §4: portrait resolution order — a PNG dropped in the user's char dir wins, otherwise the
/// app-bundled resource is used. Asserted on the resolved URI (pure, no decode) with an injected
/// file-exists probe so no real files are touched.
/// </summary>
public sealed class CharacterImageProviderTests
{
    static CharacterImageProviderTests()
    {
        // The WPF "pack://" scheme is registered by System.Windows.Application at app start; in a bare
        // test process it isn't, so parsing a pack URI throws. Register a generic parser so ResolveUri
        // can be asserted without standing up an Application (this is test-only plumbing).
        if (!UriParser.IsKnownScheme("pack"))
            UriParser.Register(new GenericUriParser(GenericUriParserOptions.GenericAuthority), "pack", -1);
    }

    private const string UserDir = @"C:\fake\VpsWatcher\assets\char";

    private static CharacterImageProvider Provider(AppearanceConfig cfg, Func<string, bool> exists)
        => new(cfg, userCharDir: UserDir, logger: null, fileExists: exists);

    [Fact]
    public void Resolves_to_bundled_resource_when_no_user_override()
    {
        var p = Provider(new AppearanceConfig(), _ => false);

        var uri = p.ResolveUri(CharacterMood.Normal);

        Assert.Equal("pack", uri.Scheme);
        Assert.EndsWith("/Assets/char/mei-hutuu.png", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolves_to_user_override_png_when_present()
    {
        var expected = Path.Combine(UserDir, "mei-obie.png");
        // Only the Critical override file "exists".
        var p = Provider(new AppearanceConfig(), path => path == expected);

        var uri = p.ResolveUri(CharacterMood.Critical);

        Assert.True(uri.IsFile);
        Assert.Equal(expected, uri.LocalPath);
    }

    [Fact]
    public void Honours_configured_file_name_for_the_user_override_lookup()
    {
        var cfg = new AppearanceConfig { Expressions = new() { ["Warning"] = "custom-warn.png" } };
        var overridePath = Path.Combine(UserDir, "custom-warn.png");
        var p = Provider(cfg, path => path == overridePath);

        var uri = p.ResolveUri(CharacterMood.Warning);

        Assert.True(uri.IsFile);
        Assert.Equal(overridePath, uri.LocalPath);
    }

    [Fact]
    public void Missing_user_override_for_configured_name_falls_back_to_bundled()
    {
        var cfg = new AppearanceConfig { Expressions = new() { ["Warning"] = "custom-warn.png" } };
        var p = Provider(cfg, _ => false); // user file absent

        var uri = p.ResolveUri(CharacterMood.Warning);

        Assert.Equal("pack", uri.Scheme);
        Assert.EndsWith("/Assets/char/custom-warn.png", uri.AbsoluteUri, StringComparison.Ordinal);
    }
}
