using System.Windows.Media;
using VpsWatcher.App.Configuration;

namespace VpsWatcher.App.Services;

/// <summary>
/// Supplies the (cached, frozen) portrait for a given mood (design §8). Abstracted so the
/// ViewModel's mood/recovery logic stays unit-testable without decoding real images.
/// </summary>
public interface ICharacterImageSource
{
    /// <summary>The image for <paramref name="mood"/>, or null if even the bundled fallback failed.</summary>
    ImageSource? ImageFor(CharacterMood mood);
}
