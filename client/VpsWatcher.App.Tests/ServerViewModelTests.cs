using System.Collections.Generic;
using VpsWatcher.App.ViewModels;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Network-free unit tests for <see cref="ServerViewModel"/> (Phase 3a, design §5.3/§6.1/§13.2).
/// We feed the ViewModel the same Core event payloads the SSH service raises
/// (<see cref="MetricsReceivedEventArgs"/> / <see cref="ConnectionStateChangedEventArgs"/>) and
/// assert the bound properties. A synchronous dispatcher stands in for the WPF UI thread.
/// </summary>
public sealed class ServerViewModelTests
{
    private static ServerViewModel NewVm() =>
        new("vps-example-1", "Example Server 1", new SynchronousDispatcher());

    private static void Feed(ServerViewModel vm, string fixture) =>
        vm.HandleMetrics(null, new MetricsReceivedEventArgs(TestData.Sample(fixture)));

    [Fact]
    public void Maps_sample_fields_to_bound_properties()
    {
        var vm = NewVm();

        Feed(vm, "sample.ndjson");

        Assert.Equal(12.4, vm.CpuPct);
        Assert.Equal(63.2, vm.MemPct);
        Assert.Equal(0.0, vm.SwapPct);
        Assert.Equal(102400, vm.RxBps);
        Assert.Equal(51200, vm.TxBps);
        Assert.Equal(0.12, vm.Load1);
        Assert.Equal(0.08, vm.Load5);
        Assert.Equal(0.05, vm.Load15);
        Assert.Equal(864000, vm.UptimeSec);

        var disk = Assert.Single(vm.Disks);
        Assert.Equal("/", disk.Mount);
        Assert.Equal(48.1, disk.UsedPct);
    }

    [Fact]
    public void Null_rates_show_measuring_not_zero()
    {
        var vm = NewVm();

        Feed(vm, "sample_measuring.ndjson");

        // The contract distinguishes "measuring/unknown" (null) from 0 (§3.4). The ViewModel must
        // not collapse null to 0: the numeric stays null and the display text says so.
        Assert.Null(vm.CpuPct);
        Assert.Null(vm.RxBps);
        Assert.Null(vm.TxBps);

        Assert.Equal("測定中", vm.CpuText);
        Assert.Equal("測定中", vm.RxText);
        Assert.Equal("測定中", vm.TxText);

        Assert.DoesNotContain("0", vm.CpuText);
    }

    [Fact]
    public void Reconciles_multiple_disks()
    {
        var vm = NewVm();

        Feed(vm, "sample_multidisk.ndjson");

        Assert.Equal(2, vm.Disks.Count);
        Assert.Equal("/", vm.Disks[0].Mount);
        Assert.Equal("/var/www", vm.Disks[1].Mount);
        Assert.Equal(67.3, vm.Disks[1].UsedPct);
    }

    [Fact]
    public void Updates_disks_in_place_across_samples()
    {
        // §13.2 partial redraw: the same mount must keep its DiskGaugeViewModel instance across
        // 1Hz updates (only the changed UsedPct notifies), not be rebuilt every second.
        var vm = NewVm();
        Feed(vm, "sample.ndjson");
        var firstDiskInstance = vm.Disks[0];

        Feed(vm, "sample.ndjson");

        Assert.Same(firstDiskInstance, vm.Disks[0]);
    }

    [Fact]
    public void HostKeyMismatch_raises_top_priority_warning_state()
    {
        var vm = NewVm();

        vm.HandleStateChanged(null, new ConnectionStateChangedEventArgs(
            ConnectionState.Connecting, ConnectionState.HostKeyMismatch, "host key mismatch"));

        Assert.Equal(ConnectionState.HostKeyMismatch, vm.ConnectionState);
        Assert.True(vm.IsHostKeyMismatch);
        Assert.False(vm.IsConnected);
        // §5.4.1: HostKeyMismatch is an active refusal (stopped), distinct from Disconnected
        // (waiting to recover). The warning text must reflect "stopped / needs attention".
        Assert.Contains("ホスト鍵", vm.StateText);
    }

    [Theory]
    [InlineData(ConnectionState.Connecting, false, false)]
    [InlineData(ConnectionState.Connected, true, false)]
    [InlineData(ConnectionState.Disconnected, false, false)]
    [InlineData(ConnectionState.HostKeyMismatch, false, true)]
    public void State_flags_track_connection_state(ConnectionState state, bool connected, bool mismatch)
    {
        var vm = NewVm();

        vm.HandleStateChanged(null, new ConnectionStateChangedEventArgs(
            ConnectionState.Disconnected, state, null));

        Assert.Equal(state, vm.ConnectionState);
        Assert.Equal(connected, vm.IsConnected);
        Assert.Equal(mismatch, vm.IsHostKeyMismatch);
    }

    [Fact]
    public void Metrics_update_notifies_only_changed_property_text()
    {
        // Partial redraw (§13.2): changing CpuPct must raise PropertyChanged for CpuPct and its
        // derived CpuText, demonstrating per-property notification rather than a whole-panel reset.
        var vm = NewVm();
        Feed(vm, "sample_measuring.ndjson"); // cpu null

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Feed(vm, "sample.ndjson"); // cpu 12.4

        Assert.Contains(nameof(ServerViewModel.CpuPct), changed);
        Assert.Contains(nameof(ServerViewModel.CpuText), changed);
    }
}
