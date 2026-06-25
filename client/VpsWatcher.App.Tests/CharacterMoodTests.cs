using System.Collections.Generic;
using System.Windows.Media;
using VpsWatcher.App.Configuration;
using VpsWatcher.App.Services;
using VpsWatcher.App.ViewModels;
using VpsWatcher.Core.Alerts;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Phase 6b §3: the one character's mood is the worst-of all servers' overall state, and a transient
/// "recovery" portrait is shown the moment everything drops back to Normal. Driven network-free via
/// the internal ServerViewModel-list ctor + a manual recovery scheduler.
/// </summary>
public sealed class CharacterMoodTests
{
    /// <summary>Returns a distinct ImageSource per mood so we can assert CharacterImage tracks mood.</summary>
    private sealed class StubImages : ICharacterImageSource
    {
        private readonly Dictionary<CharacterMood, ImageSource> _map = new();
        public CharacterMood? Last { get; private set; }
        public ImageSource? ImageFor(CharacterMood mood)
        {
            Last = mood;
            if (!_map.TryGetValue(mood, out var img))
                _map[mood] = img = new DrawingImage();
            return img;
        }
    }

    /// <summary>Captures the scheduled recovery-revert so the test fires it on demand.</summary>
    private sealed class ManualScheduler : IRecoveryScheduler
    {
        private Action? _pending;
        public bool HasPending => _pending is not null;
        public void Schedule(TimeSpan delay, Action onElapsed) => _pending = onElapsed;
        public void Cancel() => _pending = null;
        public void Fire()
        {
            var cb = _pending;
            _pending = null;
            cb?.Invoke();
        }
    }

    private static ServerViewModel Server(string id) =>
        new(id, id, new SynchronousDispatcher(), new ServerThresholds
        {
            Cpu = new double[] { 70, 85, 95 },
            Mem = new double[] { 75, 88, 95 },
            Disk = new double[] { 80, 90, 95 },
            Swap = new double[] { 25, 50, 80 },
        });

    private static void SetState(ServerViewModel vm, ConnectionState state) =>
        vm.HandleStateChanged(null, new ConnectionStateChangedEventArgs(ConnectionState.Connecting, state, null));

    [Fact]
    public void Starts_normal()
    {
        var vm = new MainViewModel(new[] { Server("a") }, images: new StubImages());
        Assert.Equal(AlertLevel.Normal, vm.WorstState);
        Assert.Equal(CharacterMood.Normal, vm.CurrentMood);
    }

    [Fact]
    public void Mood_is_worst_of_all_servers()
    {
        var a = Server("a");
        var b = Server("b");
        var vm = new MainViewModel(new[] { a, b }, images: new StubImages());

        SetState(a, ConnectionState.Disconnected);   // a = Disconnected (4)
        SetState(b, ConnectionState.HostKeyMismatch); // b = HostKeyMismatch (5) — outranks

        Assert.Equal(AlertLevel.HostKeyMismatch, vm.WorstState);
        Assert.Equal(CharacterMood.HostKeyMismatch, vm.CurrentMood);
    }

    [Fact]
    public void CharacterImage_tracks_the_current_mood()
    {
        var a = Server("a");
        var images = new StubImages();
        var vm = new MainViewModel(new[] { a }, images: images);

        SetState(a, ConnectionState.Disconnected);

        Assert.Equal(CharacterMood.Disconnected, images.Last);
        Assert.Same(images.ImageFor(CharacterMood.Disconnected), vm.CharacterImage);
    }

    [Fact]
    public void Recovery_to_normal_shows_recovery_then_reverts_after_timer()
    {
        var a = Server("a");
        var sched = new ManualScheduler();
        var vm = new MainViewModel(new[] { a }, images: new StubImages(), recovery: sched);

        SetState(a, ConnectionState.Disconnected);
        Assert.Equal(CharacterMood.Disconnected, vm.CurrentMood);

        SetState(a, ConnectionState.Connected); // recovered → all Normal
        Assert.Equal(AlertLevel.Normal, vm.WorstState);
        Assert.Equal(CharacterMood.Recovery, vm.CurrentMood); // transient yorokobi
        Assert.True(sched.HasPending);

        sched.Fire(); // the DispatcherTimer would fire here
        Assert.Equal(CharacterMood.Normal, vm.CurrentMood);
    }

    [Fact]
    public void Re_escalation_during_recovery_cancels_the_revert_and_shows_new_mood()
    {
        var a = Server("a");
        var sched = new ManualScheduler();
        var vm = new MainViewModel(new[] { a }, images: new StubImages(), recovery: sched);

        SetState(a, ConnectionState.Disconnected);
        SetState(a, ConnectionState.Connected); // → Recovery, revert pending
        Assert.Equal(CharacterMood.Recovery, vm.CurrentMood);
        Assert.True(sched.HasPending);

        SetState(a, ConnectionState.HostKeyMismatch); // escalates again before revert fires
        Assert.Equal(CharacterMood.HostKeyMismatch, vm.CurrentMood);
        Assert.False(sched.HasPending); // pending revert was cancelled

        // A stale fire (if it somehow happened) must not override the live mood.
        sched.Fire();
        Assert.Equal(CharacterMood.HostKeyMismatch, vm.CurrentMood);
    }
}
