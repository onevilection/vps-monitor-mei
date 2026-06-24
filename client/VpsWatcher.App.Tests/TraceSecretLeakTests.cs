using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using VpsWatcher.App.Configuration;
using VpsWatcher.App.ViewModels;
using VpsWatcher.Core.Configuration;

namespace VpsWatcher.App.Tests;

/// <summary>
/// The off-screen <see cref="Trace"/> diagnostic channel must be as secret-safe as the file log:
/// interpolating a whole exception (<c>{ex}</c> ⇒ <see cref="Exception.ToString"/>) would emit
/// <see cref="Exception.Message"/> / <see cref="FileNotFoundException.FileName"/> — which SSH.NET and
/// System.Text.Json embed the key path / host / JSON fragments into — to any registered Trace
/// listener (DebugView / ETW). Only the exception TYPE name may reach Trace (security review HIGH,
/// MEDIUM 1 延長 / §4).
/// </summary>
public sealed class TraceSecretLeakTests
{
    private const string ValidKnownHostKey =
        "SHA256:D5cKV/20wphMD2QliSjc7EQgc1fkk5FRHFgq0XUD5GQ";

    private sealed class CapturingListener : TraceListener
    {
        public StringBuilder Captured { get; } = new();
        public override void Write(string? message) => Captured.Append(message);
        public override void WriteLine(string? message) => Captured.AppendLine(message);
    }

    private static string CaptureTrace(Action action)
    {
        var listener = new CapturingListener();
        Trace.Listeners.Add(listener);
        try
        {
            action();
            Trace.Flush();
            return listener.Captured.ToString();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void Connection_init_failure_does_not_leak_the_key_path_to_trace()
    {
        // A missing key file ⇒ SshConnectionService ctor throws FileNotFoundException(msg, keyPath).
        // The path carries a unique secret-looking segment; it must not reach Trace — only the type.
        var secretSegment = "super_secret_watcher_key_" + Guid.NewGuid().ToString("N");
        var keyPath = Path.Combine(Path.GetTempPath(), secretSegment, "id_ed25519");
        var configs = new[]
        {
            new ServerConfig
            {
                Id = "vps-example-1",
                Host = "203.0.113.10",
                Port = 49222,
                User = "metrics",
                KeyPath = keyPath,
                KnownHostKey = ValidKnownHostKey,
            },
        };

        var captured = CaptureTrace(() =>
        {
            using var vm = new MainViewModel(configs, configError: null, new SynchronousDispatcher());
        });

        Assert.DoesNotContain(secretSegment, captured); // key path fragment must not leak
        Assert.DoesNotContain(keyPath, captured);
        Assert.Contains("FileNotFoundException", captured); // type name is allowed and useful
        Assert.Contains("vps-example-1", captured);         // server id is not a secret
    }

    [Fact]
    public void Malformed_servers_json_does_not_leak_the_path_or_parse_detail_to_trace()
    {
        // Truncated JSON whose tokens carry secret-looking fragments. The parse error is reported to
        // Trace as the exception TYPE only — never the file path or the raw parser message.
        var dir = Path.Combine(Path.GetTempPath(), "vpswatcher_trace_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "servers.json");
        File.WriteAllText(path, "{ \"host\": \"203.0.113.77\", \"keyPath\": \"C:\\\\secret\\\\my_key\" ");
        try
        {
            var captured = CaptureTrace(() =>
            {
                _ = AppServerConfigLoader.LoadFromServersJson(path, out _);
            });

            Assert.DoesNotContain(path, captured);          // full path (embeds OS username) must not leak
            Assert.DoesNotContain("203.0.113.77", captured); // secret JSON fragments must not leak
            Assert.DoesNotContain("my_key", captured);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
