using VpsWatcher.App.Services;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Test double for <see cref="IUiDispatcher"/>: runs the posted action inline so ViewModel
/// updates are observable synchronously (no WPF Dispatcher / message pump needed in tests).
/// </summary>
internal sealed class SynchronousDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
