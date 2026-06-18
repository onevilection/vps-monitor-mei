using System;
using System.IO;
using VpsWatcher.App.ViewModels;
using VpsWatcher.Core.Configuration;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Network-free tests for <see cref="MainViewModel"/> (design §5.3 / secrets discipline).
/// Focus: a failed connection init must not leak secrets onto the on-screen StatusMessage,
/// which is painted on the transparent gadget window (visible in screenshots / screen-shares).
/// </summary>
public sealed class MainViewModelTests
{
    // A genuine ssh-keygen fingerprint so ServerConfig.Validate() and the host-key verifier pass,
    // letting construction reach the key-loading step where it then fails (missing file).
    private const string ValidKnownHostKey =
        "SHA256:D5cKV/20wphMD2QliSjc7EQgc1fkk5FRHFgq0XUD5GQ";

    [Fact]
    public void Connection_init_failure_does_not_leak_key_path_to_status_message()
    {
        // A key path that looks like a secret on disk. SshConnectionService's ctor fails to load it
        // (file missing) and throws; SSH.NET's exception embeds this path. MainViewModel must NOT
        // paint the raw exception text onto StatusMessage — only a fixed message + the (non-secret)
        // server id. This test fails against the old `: {ex.Message}` behaviour.
        var secretSegment = "super_secret_watcher_key_" + Guid.NewGuid().ToString("N");
        var keyPath = Path.Combine(Path.GetTempPath(), secretSegment, "id_ed25519");

        var config = new ServerConfig
        {
            Id = "vps-example-1",
            Host = "203.0.113.10",
            Port = 49222,
            User = "metrics",
            KeyPath = keyPath,
            KnownHostKey = ValidKnownHostKey,
        };

        var vm = new MainViewModel(config, configError: null, new SynchronousDispatcher());

        Assert.False(vm.HasServer); // fell into the empty/error state
        Assert.NotNull(vm.StatusMessage);
        // No secret-bearing fragment of the key path may reach the screen.
        Assert.DoesNotContain(secretSegment, vm.StatusMessage!);
        Assert.DoesNotContain(keyPath, vm.StatusMessage!);
        // The fixed message still names which server failed (the id is not a secret).
        Assert.Contains(config.Id, vm.StatusMessage!);
    }
}
