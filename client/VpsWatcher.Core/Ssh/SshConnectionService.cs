using Renci.SshNet;
using VpsWatcher.Core.Configuration;

namespace VpsWatcher.Core.Ssh;

/// <summary>
/// One SSH connection to one server (design §5.3/§5.4/§5.4.1). N servers = N instances.
///
/// Behaviour:
/// <list type="bullet">
///   <item>Connects with a private key (empty passphrase) and pins the host key. The host key
///         is re-verified on EVERY (re)connection via <see cref="HostKeyVerifier"/> — there is
///         no "trusted once, skip later" path (§5.4.1).</item>
///   <item>Reads the agent's NDJSON from stdout line by line (forced-command runs the agent;
///         we send no command). Each parsed line raises <see cref="MetricsReceived"/>; malformed
///         lines are dropped (§4).</item>
///   <item><see cref="ConnectionState.Disconnected"/> on EOF / error / 10s wall-clock silence,
///         then reconnects with capped exponential backoff.</item>
///   <item><see cref="ConnectionState.HostKeyMismatch"/> on a host-key mismatch — auto-reconnect
///         STOPS so we never keep dialing a suspected impostor (§5.4.1).</item>
/// </list>
/// Low-load (§13.2): one async loop, no polling timers; the silence watchdog is a per-read
/// linked token, not a resident thread.
/// </summary>
public sealed class SshConnectionService : IDisposable
{
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BackoffCap = TimeSpan.FromSeconds(30);

    private readonly ServerConfig _config;
    private readonly HostKeyVerifier _verifier;
    private readonly PrivateKeyFile _privateKey;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public string ServerId => _config.Id;
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>Raised (on the read thread) for each parsed sample. Marshal to UI in a later phase.</summary>
    public event EventHandler<MetricsReceivedEventArgs>? MetricsReceived;

    /// <summary>Raised on every state transition.</summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public SshConnectionService(ServerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Fail-closed before anything connects: Validate() throws on a missing knownHostKey,
        // and HostKeyVerifier's ctor refuses an empty pin (§5.4.1/§9.1).
        _config.Validate();
        _verifier = new HostKeyVerifier(_config.KnownHostKey);

        // Our own credential (empty passphrase). This is NOT the server host key and plays no
        // part in host-key verification; it is loaded once and reused across reconnects.
        _privateKey = new PrivateKeyFile(_config.KeyPath);
    }

    public void Start()
    {
        if (_loop is not null)
            throw new InvalidOperationException("connection service already started.");

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected on stop */ }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            bool mismatch = false;

            try
            {
                SetState(ConnectionState.Connecting, $"connecting to {_config.Id}");

                var connInfo = new ConnectionInfo(
                    _config.Host, _config.Port, _config.User,
                    new PrivateKeyAuthenticationMethod(_config.User, _privateKey))
                {
                    Timeout = ConnectTimeout,
                };

                // 4.1: a brand-new SshClient (and SSH session) every attempt. The old one is
                // disposed by the `using` before the next iteration creates a new one, so no
                // previous verification result can be reused.
                using var client = new SshClient(connInfo);

                client.HostKeyReceived += (_, e) =>
                {
                    // SSH.NET defaults CanTrust = true, so deny first and grant only on a verified
                    // match — fail-closed even if Verify() were to throw mid-handler.
                    e.CanTrust = false;

                    // Runs on EVERY (re)connect, whatever caused the previous drop (EOF / error /
                    // silence). No "first time only" or cached-trust shortcut (§5.4.1).
                    var result = _verifier.Verify(e.HostKey);
                    if (result.Trusted)
                        e.CanTrust = true;
                    else
                        mismatch = true; // fingerprints are not secret, but we don't log here.
                };

                client.Connect(); // throws if the handler set CanTrust = false

                // Defensive: SSH.NET aborts the handshake when CanTrust is false (so this is
                // normally unreachable), but never proceed to use a session whose host key we
                // rejected — fail-closed regardless of library version.
                if (mismatch)
                    throw new InvalidOperationException("host key rejected during handshake.");

                attempt = 0;       // good connection -> reset backoff
                SetState(ConnectionState.Connected, $"connected to {_config.Id}");

                await StreamAsync(client, ct).ConfigureAwait(false);
                // Returned normally => EOF. Fall through to the Disconnected/backoff path.
                SetState(ConnectionState.Disconnected, "stream ended (EOF)");
            }
            catch (OperationCanceledException)
            {
                break; // stop requested
            }
            catch (Exception ex)
            {
                if (mismatch)
                {
                    // §5.4.1: actively refuse. Stop the loop entirely so we never reconnect to a
                    // suspected impostor; the warning state is held until the user intervenes.
                    SetState(ConnectionState.HostKeyMismatch,
                        "host key mismatch — auto-reconnect stopped (possible MITM, see servers.json knownHostKey)");
                    return;
                }

                // Disconnected = couldn't connect / lost the stream. Keep waiting for recovery.
                SetState(ConnectionState.Disconnected, ex.GetType().Name);
            }

            if (ct.IsCancellationRequested)
                break;

            try { await Task.Delay(Backoff(attempt++), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task StreamAsync(SshClient client, CancellationToken ct)
    {
        // Forced-command: the server ignores this (empty) command string and runs the agent,
        // which streams NDJSON to stdout — same as `ssh metrics@host` on a raw terminal (§5.4.1).
        using var cmd = client.CreateCommand(string.Empty);
        cmd.BeginExecute();
        using var reader = new StreamReader(cmd.OutputStream);

        while (!ct.IsCancellationRequested)
        {
            // Wall-clock silence watchdog (§5.4): cancel the read if no line arrives within the
            // window. Uses a timer (CancelAfter), never the sample `ts`.
            using var silenceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            silenceCts.CancelAfter(SilenceTimeout);

            string? line;
            try
            {
                line = await reader.ReadLineAsync(silenceCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"no data for {SilenceTimeout.TotalSeconds:0}s (wall-clock watchdog).");
            }

            if (line is null)
                return; // EOF

            if (line.Length == 0)
                continue;

            if (NdjsonParser.TryParse(line, out var sample) && sample is not null)
                MetricsReceived?.Invoke(this, new MetricsReceivedEventArgs(sample));
            // else: malformed line dropped (§4), keep reading.
        }

        ct.ThrowIfCancellationRequested();
    }

    private static TimeSpan Backoff(int attempt)
    {
        // 1s, 2s, 4s, ... capped at 30s (exponential with a ceiling).
        double seconds = BackoffBase.TotalSeconds * Math.Pow(2, Math.Min(attempt, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, BackoffCap.TotalSeconds));
    }

    private void SetState(ConnectionState next, string? detail)
    {
        if (State == next)
            return;

        var old = State;
        State = next;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(old, next, detail));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _privateKey.Dispose();
    }
}
