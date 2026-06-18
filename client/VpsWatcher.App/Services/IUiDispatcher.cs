namespace VpsWatcher.App.Services;

/// <summary>
/// Marshals an action onto the UI thread. Core events (MetricsReceived / StateChanged) fire on
/// the SSH read thread (design §5.3); ViewModels post their property updates through this so the
/// WPF Dispatcher is the only thing touching bindable state. Abstracted so ViewModels stay
/// unit-testable without a running Dispatcher (tests supply a synchronous implementation).
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Runs <paramref name="action"/> on the UI thread (inline if already on it).</summary>
    void Post(Action action);
}
