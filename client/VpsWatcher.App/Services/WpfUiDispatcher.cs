using System.Windows.Threading;

namespace VpsWatcher.App.Services;

/// <summary>
/// WPF implementation of <see cref="IUiDispatcher"/>: marshals onto a <see cref="Dispatcher"/>
/// (the UI thread). Runs inline when already on the UI thread; otherwise posts asynchronously
/// (<see cref="DispatcherPriority.Background"/>) so the SSH read thread is never blocked and the
/// 1Hz updates don't contend with input (§13.2).
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(DispatcherPriority.Background, action);
    }
}
