using System;
using System.IO;
using VpsWatcher.App.ViewModels;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Network-free tests for <see cref="MainViewModel"/> N-server wiring and the secrets discipline
/// (design §5.3 / §5.4.1 / MEDIUM 1). Connection init failures must stay fail-soft (other servers
/// keep running) and never leak secrets onto the on-screen StatusMessage, which is painted on the
/// transparent gadget window (visible in screenshots / screen-shares).
/// </summary>
public sealed class MainViewModelTests
{
    // A genuine ssh-keygen fingerprint so ServerConfig.Validate() and the host-key verifier pass.
    private const string ValidKnownHostKey =
        "SHA256:D5cKV/20wphMD2QliSjc7EQgc1fkk5FRHFgq0XUD5GQ";

    private static ServerConfig Config(string id, string keyPath) => new()
    {
        Id = id,
        Host = "203.0.113.10",
        Port = 49222,
        User = "metrics",
        KeyPath = keyPath,
        KnownHostKey = ValidKnownHostKey,
    };

    /// <summary>
    /// A temp file that merely EXISTS — its contents are never a valid key. Since MEDIUM 3 stopped
    /// the ctor from parsing the key (it only checks the path exists; the PrivateKeyFile is built
    /// per-connection on a background thread), such a file lets <see cref="SshConnectionService"/>
    /// construct successfully without any network.
    /// </summary>
    private sealed class TempKeyFile : IDisposable
    {
        public string Path { get; }
        private readonly string _dir;
        public TempKeyFile()
        {
            _dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "vpswatcher_key_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            Path = System.IO.Path.Combine(_dir, "id_ed25519");
            File.WriteAllText(Path, "not a real key");
        }
        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Two_servers_create_two_panels_in_array_order()
    {
        using var keyA = new TempKeyFile();
        using var keyB = new TempKeyFile();
        var configs = new[] { Config("vps-a", keyA.Path), Config("vps-b", keyB.Path) };

        using var vm = new MainViewModel(configs, configError: null, new SynchronousDispatcher());

        Assert.True(vm.HasServer);
        Assert.Equal(2, vm.Servers.Count);
        Assert.Equal("vps-a", vm.Servers[0].Id); // servers.json array order (§5.3)
        Assert.Equal("vps-b", vm.Servers[1].Id);
        Assert.NotSame(vm.Servers[0], vm.Servers[1]); // independent panels
        Assert.Null(vm.StatusMessage);                // a server is shown ⇒ no empty state
    }

    [Fact]
    public void One_invalid_server_is_skipped_while_the_valid_one_still_loads()
    {
        // Fail-soft (§5.4.1): the second config points at a missing key file, so its
        // SshConnectionService ctor throws — that server is skipped, but the first still loads.
        using var keyA = new TempKeyFile();
        var missingKey = Path.Combine(
            Path.GetTempPath(), "vpswatcher_absent_" + Guid.NewGuid().ToString("N"), "id_ed25519");
        var configs = new[] { Config("vps-ok", keyA.Path), Config("vps-broken", missingKey) };

        using var vm = new MainViewModel(configs, configError: null, new SynchronousDispatcher());

        Assert.True(vm.HasServer);
        Assert.Single(vm.Servers);
        Assert.Equal("vps-ok", vm.Servers[0].Id);
        Assert.Null(vm.StatusMessage); // a working server remains ⇒ no banner
    }

    [Fact]
    public void Connection_init_failure_does_not_leak_key_path_to_status_message()
    {
        // A key path that looks like a secret on disk. With no working server left, MainViewModel
        // shows the empty state — but it must name only the (non-secret) server id, never the raw
        // exception text / key path that SSH.NET-style errors embed. StatusMessage is painted on the
        // transparent gadget (so it leaks into screenshots / screen-shares).
        var secretSegment = "super_secret_watcher_key_" + Guid.NewGuid().ToString("N");
        var keyPath = Path.Combine(Path.GetTempPath(), secretSegment, "id_ed25519");
        var configs = new[] { Config("vps-example-1", keyPath) };

        using var vm = new MainViewModel(configs, configError: null, new SynchronousDispatcher());

        Assert.False(vm.HasServer); // fell into the empty/error state
        Assert.NotNull(vm.StatusMessage);
        // No secret-bearing fragment of the key path may reach the screen.
        Assert.DoesNotContain(secretSegment, vm.StatusMessage!);
        Assert.DoesNotContain(keyPath, vm.StatusMessage!);
        // The fixed message still names which server failed (the id is not a secret).
        Assert.Contains("vps-example-1", vm.StatusMessage!);
    }

    [Fact]
    public void Empty_config_list_shows_the_supplied_empty_state_message()
    {
        using var vm = new MainViewModel(
            Array.Empty<ServerConfig>(), configError: "接続先が未設定です。", new SynchronousDispatcher());

        Assert.False(vm.HasServer);
        Assert.Equal("接続先が未設定です。", vm.StatusMessage);
    }

    [Fact]
    public void A_state_change_on_one_panel_does_not_propagate_to_another()
    {
        // Each ServerViewModel is its own bound state (§5.3); pushing a host-key mismatch to one must
        // not move a sibling. Exercised at the ViewModel layer so the assertion is deterministic and
        // independent of any live background reconnect loop.
        var a = new ServerViewModel("vps-a", "A", new SynchronousDispatcher());
        var b = new ServerViewModel("vps-b", "B", new SynchronousDispatcher());

        a.HandleStateChanged(this, new ConnectionStateChangedEventArgs(
            ConnectionState.Connecting, ConnectionState.HostKeyMismatch, detail: null));

        Assert.True(a.IsHostKeyMismatch);
        Assert.False(b.IsHostKeyMismatch);
    }
}
