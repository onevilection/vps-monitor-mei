using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VpsWatcher.App.Configuration;
using VpsWatcher.Core.Logging;

namespace VpsWatcher.App.Services;

/// <summary>
/// Resolves and caches the character portraits (design §8 / §13.2). Each mood's PNG is decoded once
/// and <see cref="Freezable.Freeze">frozen</see>, then reused — switching expressions only swaps the
/// already-decoded <see cref="ImageSource"/>, never re-decodes.
///
/// Resolution order per mood (§8 user-overridable): (1) a PNG the user dropped in
/// <c>%APPDATA%\VpsWatcher\assets\char\{file}</c>, else (2) the app-bundled default compiled into
/// resources. Fail-soft: a missing / corrupt PNG falls back to the bundled file, then to the bundled
/// Normal portrait, so a bad asset never crashes the gadget.
/// </summary>
public sealed class CharacterImageProvider : ICharacterImageSource
{
    private const int DecodeWidth = 140; // matches the 6a display width; smaller decode = less memory

    private readonly AppearanceConfig _config;
    private readonly string _userCharDir;
    private readonly Func<string, bool> _fileExists;
    private readonly IAppLogger? _logger;
    private readonly Dictionary<CharacterMood, ImageSource?> _cache = new();

    public CharacterImageProvider(
        AppearanceConfig config,
        string? userCharDir = null,
        IAppLogger? logger = null,
        Func<string, bool>? fileExists = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _userCharDir = userCharDir ?? AppearanceStore.DefaultUserCharDir;
        _logger = logger;
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>The URI a mood resolves to before decoding: the user-override file when present,
    /// otherwise the bundled resource. Pure (no decode) so the resolution order is unit-testable.</summary>
    public Uri ResolveUri(CharacterMood mood)
    {
        var file = _config.FileNameFor(mood);
        var userPath = Path.Combine(_userCharDir, file);
        return _fileExists(userPath)
            ? new Uri(userPath, UriKind.Absolute)
            : EmbeddedUri(file);
    }

    /// <summary>Decodes + freezes every mood up front (§13.2) so the first state change is instant.</summary>
    public void PreloadAll()
    {
        foreach (CharacterMood mood in Enum.GetValues(typeof(CharacterMood)))
            ImageFor(mood);
    }

    public ImageSource? ImageFor(CharacterMood mood)
    {
        if (_cache.TryGetValue(mood, out var cached))
            return cached;

        // (1) resolved (user override or bundled) → (2) bundled default for this mood → (3) bundled Normal.
        var image = LoadFrozen(ResolveUri(mood))
            ?? LoadFrozen(EmbeddedUri(_config.FileNameFor(mood)))
            ?? LoadFrozen(EmbeddedUri(AppearanceConfig.DefaultExpressions[CharacterMood.Normal]));

        _cache[mood] = image;
        return image;
    }

    private static Uri EmbeddedUri(string fileName)
        => new($"pack://application:,,,/VpsWatcher.App;component/Assets/char/{fileName}", UriKind.Absolute);

    private ImageSource? LoadFrozen(Uri uri)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad; // decode now, release the stream
            bmp.DecodePixelWidth = DecodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            // Reason + scheme only (no full path → no secrets/PII leak, §4).
            _logger?.Log(LogSeverity.Warning, "character image load failed; falling back",
                new Dictionary<string, object?> { ["reason"] = ex.GetType().Name, ["scheme"] = uri.Scheme });
            return null;
        }
    }
}
