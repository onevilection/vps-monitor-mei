namespace VpsWatcher.App.Services;

/// <summary>
/// One-shot delayed callback used for the transient "recovery" expression (design §8): show the
/// recovery portrait, then revert after a short delay. Abstracted so the ViewModel's recovery logic
/// is unit-testable (a fake fires the callback synchronously) without a real <c>DispatcherTimer</c>.
/// Scheduling again replaces any pending callback; <see cref="Cancel"/> drops it (e.g. when the
/// state escalates again before the revert fires).
/// </summary>
public interface IRecoveryScheduler
{
    void Schedule(TimeSpan delay, Action onElapsed);
    void Cancel();
}
