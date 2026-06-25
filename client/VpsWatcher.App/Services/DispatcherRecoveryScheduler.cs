using System.Windows.Threading;

namespace VpsWatcher.App.Services;

/// <summary>
/// <see cref="IRecoveryScheduler"/> backed by a single reusable <see cref="DispatcherTimer"/> on the
/// UI thread (design §8 / §13.2 — no per-tick polling; it ticks exactly once then stops). One timer
/// instance is reused across recoveries to avoid churn.
/// </summary>
public sealed class DispatcherRecoveryScheduler : IRecoveryScheduler
{
    private readonly DispatcherTimer _timer;
    private Action? _onElapsed;

    public DispatcherRecoveryScheduler(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var cb = _onElapsed;
            _onElapsed = null;
            cb?.Invoke();
        };
    }

    public void Schedule(TimeSpan delay, Action onElapsed)
    {
        _timer.Stop();
        _onElapsed = onElapsed;
        _timer.Interval = delay;
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _onElapsed = null;
    }
}
