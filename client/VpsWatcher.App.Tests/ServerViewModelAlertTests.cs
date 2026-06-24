using VpsWatcher.App.ViewModels;
using VpsWatcher.Core.Alerts;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Schema;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.App.Tests;

/// <summary>
/// The ServerViewModel owns a per-server <see cref="AlertStateMachine"/> (design §5.3) and surfaces
/// its overall state as a bindable <c>AlertState</c> for the next phase (character / colour). All
/// machine input happens on the UI thread via the existing dispatcher marshaling, so no extra
/// threading. Here the dispatcher runs inline (SynchronousDispatcher).
/// </summary>
public sealed class ServerViewModelAlertTests
{
    private static ServerThresholds Std() => new()
    {
        Cpu = new double[] { 70, 85, 95 },
        Mem = new double[] { 75, 88, 95 },
        Disk = new double[] { 80, 90, 95 },
        Swap = new double[] { 25, 50, 80 },
    };

    private static Sample CpuSample(double cpu) => new()
    {
        Id = "vps-1",
        CpuPct = cpu,
        Mem = new Mem { UsedPct = 1 },
        Swap = new Swap { UsedPct = 0 },
        Disk = new[] { new DiskEntry { Mount = "/", UsedPct = 1 } },
    };

    [Fact]
    public void AlertState_starts_normal()
    {
        var vm = new ServerViewModel("vps-1", "A", new SynchronousDispatcher(), Std());
        Assert.Equal(AlertLevel.Normal, vm.AlertState);
    }

    [Fact]
    public void AlertState_reflects_metric_escalation_after_debounce()
    {
        var vm = new ServerViewModel("vps-1", "A", new SynchronousDispatcher(), Std());
        for (int i = 0; i < 3; i++)
            vm.HandleMetrics(this, new MetricsReceivedEventArgs(CpuSample(86)));
        Assert.Equal(AlertLevel.Warning, vm.AlertState);
    }

    [Fact]
    public void AlertState_reflects_hostkeymismatch_immediately()
    {
        var vm = new ServerViewModel("vps-1", "A", new SynchronousDispatcher(), Std());
        vm.HandleStateChanged(this, new ConnectionStateChangedEventArgs(
            ConnectionState.Connecting, ConnectionState.HostKeyMismatch, detail: null));
        Assert.Equal(AlertLevel.HostKeyMismatch, vm.AlertState);
    }
}
