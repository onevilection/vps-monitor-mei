using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VpsWatcher.App.Services;
using VpsWatcher.Core.Schema;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.App.ViewModels;

/// <summary>
/// One server's bound state (design §5.3). Subscribes to a <see cref="SshConnectionService"/>'s
/// <see cref="SshConnectionService.MetricsReceived"/> / <see cref="SshConnectionService.StateChanged"/>
/// (wired by <see cref="HandleMetrics"/> / <see cref="HandleStateChanged"/>) which fire on the SSH
/// read thread, and marshals every property update onto the UI thread via <see cref="IUiDispatcher"/>.
///
/// Low-load / partial redraw (§13.2): only changed properties notify (CommunityToolkit
/// <c>[ObservableProperty]</c>), derived display text is recomputed via
/// <c>[NotifyPropertyChangedFor]</c>, and disk rows are reconciled in place rather than rebuilt.
///
/// Rate fields (cpu/rx/tx) are nullable: <c>null</c> = "measuring/unknown", kept distinct from 0
/// (§3.4) — the numeric stays null and the *_Text property shows 測定中.
/// </summary>
public sealed partial class ServerViewModel : ObservableObject
{
    private const string Measuring = "測定中";

    private readonly IUiDispatcher _dispatcher;

    public ServerViewModel(string id, string? label, IUiDispatcher dispatcher)
    {
        Id = id;
        Label = string.IsNullOrWhiteSpace(label) ? id : label!;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>Stable server id (matches NDJSON <c>id</c> / servers.json). Set once.</summary>
    public string Id { get; }

    /// <summary>Display label; falls back to <see cref="Id"/>. Set once.</summary>
    public string Label { get; }

    // ───────────────────────── metrics (bindable) ─────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuText))]
    private double? _cpuPct;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemText))]
    private double _memPct;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwapText))]
    private double _swapPct;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RxText))]
    private double? _rxBps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxText))]
    private double? _txBps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadText))]
    private double _load1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadText))]
    private double _load5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadText))]
    private double _load15;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UptimeText))]
    private long _uptimeSec;

    /// <summary>Per-mount gauges, reconciled in place across samples (§13.2).</summary>
    public ObservableCollection<DiskGaugeViewModel> Disks { get; } = new();

    // ───────────────────────── connection state (bindable) ─────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyPropertyChangedFor(nameof(IsHostKeyMismatch))]
    private ConnectionState _connectionState = ConnectionState.Connecting;

    // ───────────────────────── derived display text ─────────────────────────

    public string CpuText => CpuPct is { } v ? $"{v:0.0}%" : Measuring;
    public string MemText => $"{MemPct:0.0}%";
    public string SwapText => $"{SwapPct:0.0}%";
    public string RxText => RxBps is { } v ? FormatRate(v) : Measuring;
    public string TxText => TxBps is { } v ? FormatRate(v) : Measuring;
    public string LoadText => $"{Load1:0.00} / {Load5:0.00} / {Load15:0.00}";
    public string UptimeText => FormatUptime(UptimeSec);

    /// <summary>
    /// Human-readable connection state (§5.4.1/§6.1). HostKeyMismatch (active refusal, stopped) is
    /// worded distinctly from Disconnected (waiting to recover); the View renders it as the
    /// top-priority warning.
    /// </summary>
    public string StateText => ConnectionState switch
    {
        ConnectionState.Connecting => "接続中…",
        ConnectionState.Connected => "接続済み",
        ConnectionState.Disconnected => "接続が切れました（再接続中）",
        ConnectionState.HostKeyMismatch => "⚠️ ホスト鍵不一致（なりすまし疑い）— 接続を停止しました（要確認）",
        _ => ConnectionState.ToString(),
    };

    public bool IsConnecting => ConnectionState == ConnectionState.Connecting;
    public bool IsConnected => ConnectionState == ConnectionState.Connected;
    public bool IsDisconnected => ConnectionState == ConnectionState.Disconnected;

    /// <summary>True on host-key mismatch — the top-priority MITM warning (§6.1).</summary>
    public bool IsHostKeyMismatch => ConnectionState == ConnectionState.HostKeyMismatch;

    // ───────────────────────── Core event handlers ─────────────────────────
    // Fire on the SSH read thread; marshal to the UI thread before touching bindable state.

    public void HandleMetrics(object? sender, MetricsReceivedEventArgs e)
        => _dispatcher.Post(() => ApplySample(e.Sample));

    public void HandleStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        => _dispatcher.Post(() => ConnectionState = e.NewState);

    private void ApplySample(Sample s)
    {
        CpuPct = s.CpuPct;
        MemPct = s.Mem.UsedPct;
        SwapPct = s.Swap.UsedPct;
        RxBps = s.Net.RxBps;
        TxBps = s.Net.TxBps;

        // load is always 3 elements (schema §2), but stay defensive on count.
        Load1 = s.Load.Count > 0 ? s.Load[0] : 0;
        Load5 = s.Load.Count > 1 ? s.Load[1] : 0;
        Load15 = s.Load.Count > 2 ? s.Load[2] : 0;

        UptimeSec = s.UptimeSec;

        ReconcileDisks(s.Disk);
    }

    /// <summary>
    /// Updates disk gauges in place keyed by mount (§13.2): existing mounts keep their instance and
    /// only the changed percentage notifies; new mounts are added, vanished mounts removed. Disk
    /// count is not fixed (§4), so we never assume a specific number of rows.
    /// </summary>
    private void ReconcileDisks(IReadOnlyList<DiskEntry> disks)
    {
        for (int i = 0; i < disks.Count; i++)
        {
            var entry = disks[i];
            var existing = Disks.FirstOrDefault(d => d.Mount == entry.Mount);
            if (existing is null)
            {
                existing = new DiskGaugeViewModel(entry.Mount);
                Disks.Insert(Math.Min(i, Disks.Count), existing);
            }
            else if (Disks.IndexOf(existing) != i)
            {
                Disks.Move(Disks.IndexOf(existing), Math.Min(i, Disks.Count - 1));
            }

            existing.UsedPct = entry.UsedPct;
            existing.UsedGb = entry.UsedGb;
            existing.TotalGb = entry.TotalGb;
        }

        // Drop mounts no longer reported.
        for (int i = Disks.Count - 1; i >= 0; i--)
        {
            if (!disks.Any(e => e.Mount == Disks[i].Mount))
                Disks.RemoveAt(i);
        }
    }

    // ───────────────────────── formatting helpers ─────────────────────────

    private static string FormatRate(double bytesPerSec)
    {
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        double value = bytesPerSec;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.0} {units[unit]}";
    }

    private static string FormatUptime(long seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.Days > 0
            ? $"{t.Days}d {t.Hours}h {t.Minutes}m"
            : $"{t.Hours}h {t.Minutes}m";
    }
}
