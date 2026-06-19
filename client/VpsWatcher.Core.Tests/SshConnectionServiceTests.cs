using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.Core.Tests;

/// <summary>
/// Network-free tests for <see cref="SshConnectionService"/> construction (design §5.4 / §9.1).
/// Real connection behaviour is exercised manually via VpsWatcher.ConsoleTest; here we only
/// assert the ctor's key-loading contract.
/// </summary>
public class SshConnectionServiceTests
{
    // A genuine ssh-keygen fingerprint (see HostKeyVerificationTests) so Validate()/the verifier
    // ctor pass and we reach the key-loading step.
    private const string ValidKnownHostKey =
        "SHA256:D5cKV/20wphMD2QliSjc7EQgc1fkk5FRHFgq0XUD5GQ";

    private static ServerConfig ConfigWithKeyPath(string keyPath) => new()
    {
        Id = "vps-example-1",
        Host = "203.0.113.10",
        Port = 49222,
        User = "metrics",
        KeyPath = keyPath,
        KnownHostKey = ValidKnownHostKey,
    };

    [Fact]
    public void KeyPath_environment_variables_are_expanded_before_loading_the_key()
    {
        // A keyPath written the documented way (design §9.1 / README use %USERPROFILE%\.ssh\...)
        // must be expanded before SSH.NET opens it; otherwise the literal "%VAR%\..." path never
        // resolves and a user who copied the example can never connect.
        //
        // We define our own env var (deterministic, cross-platform: .NET's ExpandEnvironmentVariables
        // honours %NAME% on every OS) pointing at a guaranteed-missing directory, so the ctor throws
        // while loading the key. The failure must reference the EXPANDED path (proving expansion
        // happened) and never the literal "%VAR%" token.
        var varName = "VPSWATCHER_TEST_KEYDIR_" + Guid.NewGuid().ToString("N");
        var missingDir = Path.Combine(
            Path.GetTempPath(), "vpswatcher_no_such_dir_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(varName, missingDir);
        try
        {
            var token = $"%{varName}%/id_ed25519";
            var expanded = Environment.ExpandEnvironmentVariables(token);
            Assert.NotEqual(token, expanded); // sanity: the token really does expand

            var ex = Record.Exception(() => new SshConnectionService(ConfigWithKeyPath(token)));

            Assert.NotNull(ex);
            var detail = ex!.ToString();
            Assert.Contains(missingDir, detail);     // loaded the expanded path...
            Assert.DoesNotContain(varName, detail);  // ...not the literal %VAR% token
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void Ctor_only_checks_the_key_path_exists_and_does_not_parse_it()
    {
        // MEDIUM 3: the PrivateKeyFile is created per-connection inside the reconnect loop (same scope
        // as its SshClient), never built once in the ctor and shared across reconnects — so there is
        // no Dispose/handshake race. As a code-level guarantee, the ctor must NOT parse the key: a
        // file that EXISTS but is not a valid key therefore constructs cleanly (it would only fail
        // later, per-connection). A genuinely missing path still fails fast (asserted above).
        var dir = Path.Combine(Path.GetTempPath(), "vpswatcher_key_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "id_ed25519");
        File.WriteAllText(path, "not a real private key");
        try
        {
            var ex = Record.Exception(() =>
            {
                using var service = new SshConnectionService(ConfigWithKeyPath(path));
            });

            Assert.Null(ex); // ctor succeeded: the key was not parsed/held — no shared PrivateKeyFile
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Silent_stream_times_out_via_wall_clock_even_when_the_read_ignores_cancellation()
    {
        // Reproduces the 4-hour freeze root cause and its fix. SSH.NET's PipeStream.Read is a blocking
        // synchronous read reached via the base Stream.ReadAsync fallback, so ReadLineAsync(token)
        // never honours the token — a silent-but-open channel parks it forever.
        var reader = new NeverCompletingReader();

        // (a) THE TRAP: cancelling the token does NOT complete a PipeStream-style read. This is exactly
        // why the old `ReadLineAsync(silenceCts.Token)` + CancelAfter watchdog could never fire.
        using (var probe = new CancellationTokenSource())
        {
            probe.CancelAfter(TimeSpan.FromMilliseconds(150));
            var read = reader.ReadLineAsync(probe.Token).AsTask();
            var raced = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.NotSame(read, raced);   // still pending well after cancellation — the watchdog is blind
            Assert.False(read.IsCompleted);
        }

        // (b) THE FIX: PumpLinesAsync enforces the silence window with a real wall-clock timer raced
        // against the read, and on timeout it ABORTS the connection (to unwind the parked read) and
        // throws TimeoutException — which RunAsync maps to Disconnected → reconnect. No infinite wait.
        var dir = Path.Combine(Path.GetTempPath(), "vpswatcher_key_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var keyPath = Path.Combine(dir, "id_ed25519");
        File.WriteAllText(keyPath, "not a real key"); // ctor only stats the path (MEDIUM 3)
        try
        {
            using var svc = new SshConnectionService(ConfigWithKeyPath(keyPath));
            int aborts = 0;
            var silence = TimeSpan.FromMilliseconds(300);
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // safety net only

            var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
                svc.PumpLinesAsync(reader, abort: () => Interlocked.Increment(ref aborts), silence, ct.Token));

            Assert.Equal(1, aborts);                    // tore down the connection to unwind the parked read
            Assert.False(ct.IsCancellationRequested);   // fired on the 300ms timer, far under the 10s net
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A reader whose async read never completes and ignores its <see cref="CancellationToken"/> —
    /// the in-test stand-in for SSH.NET's <c>PipeStream</c>, whose synchronous blocking Read (reached
    /// via the base <c>Stream.ReadAsync</c> fallback) cannot be cancelled mid-read.
    /// </summary>
    private sealed class NeverCompletingReader : TextReader
    {
        private readonly TaskCompletionSource<string?> _never = new();
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            => new(_never.Task);
        public override Task<string?> ReadLineAsync() => _never.Task;
    }
}
