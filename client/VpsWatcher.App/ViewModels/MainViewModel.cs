using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VpsWatcher.App.Services;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.App.ViewModels;

/// <summary>
/// Root window ViewModel (design §5.3). Holds N <see cref="ServerViewModel"/>s in
/// <see cref="Servers"/> (rendered by the View's ItemsControl) and owns one
/// <see cref="SshConnectionService"/> per server, wired to its ViewModel and started independently.
///
/// Fail-soft (§5.4/§5.4.1): each server connects, reconnects and verifies its host key on its own —
/// one server's drop / HostKeyMismatch never affects the others. If a server's connection fails to
/// even initialise (e.g. missing key file), it is skipped with the detail sent to Trace; the
/// remaining servers still run. Only when NO server can be shown does the empty-state
/// <see cref="StatusMessage"/> appear, and it never carries secrets or paths (MEDIUM 1).
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    // One (service, vm) pair per running server, so Dispose can unwire + tear them all down.
    private readonly List<(SshConnectionService Service, ServerViewModel Vm)> _connections = new();

    /// <summary>All server panels, in servers.json array order (design §5.3).</summary>
    public ObservableCollection<ServerViewModel> Servers { get; } = new();

    /// <summary>Set when there is no server to show (none configured / all failed). Drives the empty state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServer))]
    private string? _statusMessage;

    public bool HasServer => Servers.Count > 0;

    /// <summary>
    /// Always-on-top toggle (design §5.2.1). Bound two-way to the window's <c>Topmost</c> and to the
    /// pin toggle; persisted to state.json (§9.2). Window-level setting (one window).
    /// </summary>
    [ObservableProperty]
    private bool _alwaysOnTop;

    public MainViewModel(
        IReadOnlyList<ServerConfig> configs, string? configError, IUiDispatcher dispatcher)
    {
        if (configs is null || configs.Count == 0)
        {
            StatusMessage = configError ?? "接続先が未設定です。";
            return;
        }

        var failedIds = new List<string>();
        foreach (var config in configs)
        {
            try
            {
                var vm = new ServerViewModel(config.Id, config.Label, dispatcher);

                // SshConnectionService validates the config and resolves the key path in its ctor —
                // surface any per-server failure by skipping just that server (below), never by
                // crashing startup or taking the other servers down with it.
                var service = new SshConnectionService(config);
                service.MetricsReceived += vm.HandleMetrics;
                service.StateChanged += vm.HandleStateChanged;

                _connections.Add((service, vm));
                Servers.Add(vm);
                service.Start();
            }
            catch (Exception ex)
            {
                // Never surface the raw exception text on screen. SSH.NET's PrivateKeyFile / connect
                // exceptions can embed the private-key path, and the empty-state message is painted on
                // the transparent gadget (so it leaks into screenshots / screen-shares). Record only
                // the (non-secret) server id and send the full detail to Trace, which has no on-screen
                // surface and writes no persistent log file (MEDIUM 1 / §5.4.1 / secrets).
                System.Diagnostics.Trace.TraceError(
                    $"connection init failed for server '{config.Id}': {ex}");
                failedIds.Add(config.Id);
            }
        }

        // Empty state only when nothing could be shown. Partial failures stay in Trace so a single
        // bad entry doesn't push a banner over the servers that are working. Ids aren't secret.
        if (Servers.Count == 0)
        {
            StatusMessage = failedIds.Count > 0
                ? $"接続の初期化に失敗しました（{string.Join(", ", failedIds)}）。詳細はデバッグ出力を参照してください。"
                : (configError ?? "接続先が未設定です。");
        }
    }

    /// <summary>How long shutdown waits for the reconnect loops to wind down before giving up.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(3);

    public void Dispose()
    {
        // Unwire first so a late event can't touch a ViewModel mid-teardown.
        foreach (var (service, vm) in _connections)
        {
            service.MetricsReceived -= vm.HandleMetrics;
            service.StateChanged -= vm.HandleStateChanged;
        }

        // Bundle the cancellation: StopAsync cancels each loop's token, then we await them together
        // (bounded) so no SSH read loop / connection is left running past window close — instead of
        // only signalling cancel and racing process exit. Loops use ConfigureAwait(false) and never
        // need the UI thread, so blocking here can't deadlock. Best-effort: a hung loop must not
        // wedge shutdown, hence the grace timeout.
        var stops = _connections.Select(c => c.Service.StopAsync()).ToArray();
        try { Task.WaitAll(stops, ShutdownGrace); }
        catch { /* swallow on shutdown: we still Dispose below regardless */ }

        foreach (var (service, _) in _connections)
            service.Dispose();
        _connections.Clear();
    }
}
