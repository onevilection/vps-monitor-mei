using Renci.SshNet;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Logging;

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

    // SSH-level keep-alive (§5.4): actively probe the peer so a half-open connection (socket alive,
    // no data, no FIN/RST — e.g. NAT idle-drop or sleep/resume) is surfaced as an error instead of
    // hanging silently. Half the silence window so a dead peer is normally detected before the
    // wall-clock watchdog has to abort. One tiny packet every few seconds only when otherwise idle —
    // negligible against the 1 Hz NDJSON stream, so it does not violate the low-load budget (§13.2).
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(5);

    // How often the (debug-only) received-data heartbeat is summarised, so the 1 Hz stream is logged
    // as one line every few seconds instead of 60+ lines/min — keeps even debug_mode within the
    // low-load budget (§13.2 / instruction §6 Debug).
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    private readonly ServerConfig _config;
    private readonly HostKeyVerifier _verifier;
    private readonly string _keyPath;
    private readonly IAppLogger? _logger;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    // Heartbeat accounting (per connection; reset implicitly when a new pump loop starts).
    private DateTime _lastHeartbeatUtc;
    private int _samplesSinceHeartbeat;

    public string ServerId => _config.Id;
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>Raised (on the read thread) for each parsed sample. Marshal to UI in a later phase.</summary>
    public event EventHandler<MetricsReceivedEventArgs>? MetricsReceived;

    /// <summary>Raised on every state transition.</summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public SshConnectionService(ServerConfig config, IAppLogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;

        // Fail-closed before anything connects: Validate() throws on a missing knownHostKey,
        // and HostKeyVerifier's ctor refuses an empty pin (§5.4.1/§9.1).
        _config.Validate();
        _verifier = new HostKeyVerifier(_config.KnownHostKey);

        // Resolve the key path once, expanding environment variables so values written the design
        // §9.1 / README way (e.g. "%USERPROFILE%\.ssh\watcher_ed25519") become a real path before
        // SSH.NET opens the file (MEDIUM 2). Fail-fast here if the file is missing, so a mis-configured
        // server is excluded at startup instead of looping forever in reconnect.
        //
        // MEDIUM 3: we do NOT build/hold a long-lived PrivateKeyFile here. The credential is created
        // per-connection inside the reconnect loop (same scope as its SshClient) so it is never shared
        // across connections — removing the Dispose/handshake race between a reconnect and an in-flight
        // auth that a single reused instance could hit. We only stat the path at construction.
        _keyPath = Environment.ExpandEnvironmentVariables(_config.KeyPath);
        if (!File.Exists(_keyPath))
            throw new FileNotFoundException(
                $"private key not found for server '{_config.Id}'.", _keyPath);
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

                // MEDIUM 3: a fresh PrivateKeyFile per attempt, in the SAME scope as the SshClient.
                // Both are disposed by their `using` before the next iteration, so the credential is
                // never shared between connections and can't be disposed from under an in-flight
                // handshake. Creation cost is paid only on (re)connect, never at 1Hz (§13.2).
                using var privateKey = new PrivateKeyFile(_keyPath);

                var connInfo = new ConnectionInfo(
                    _config.Host, _config.Port, _config.User,
                    new PrivateKeyAuthenticationMethod(_config.User, privateKey))
                {
                    Timeout = ConnectTimeout,
                };

                // 4.1: a brand-new SshClient (and SSH session) every attempt. The old one is
                // disposed by the `using` before the next iteration creates a new one, so no
                // previous verification result can be reused.
                using var client = new SshClient(connInfo);

                // Actively detect dead peers (half-open connections) rather than hang on a silent
                // socket forever (§5.4) — see KeepAliveInterval.
                client.KeepAliveInterval = KeepAliveInterval;

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
                // The exception (type name + stack only — never its Message, §4) is threaded to the
                // logger so a connection drop is debuggable after the fact.
                SetState(ConnectionState.Disconnected, ex.GetType().Name, ex);
            }

            if (ct.IsCancellationRequested)
                break;

            var delay = Backoff(attempt);
            _logger?.Log(LogSeverity.Warning, "reconnect scheduled", new Dictionary<string, object?>
            {
                ["server_id"] = ServerId,
                ["attempt"] = attempt + 1,
                ["backoff_sec"] = delay.TotalSeconds,
            });
            attempt++;

            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task StreamAsync(SshClient client, CancellationToken ct)
    {
        // Forced-command: the server ignores this (empty) command string and runs the agent,
        // which streams NDJSON to stdout — same as `ssh metrics@host` on a raw terminal (§5.4.1).
        var cmd = client.CreateCommand(string.Empty);
        var reader = new StreamReader(cmd.OutputStream);
        try
        {
            cmd.BeginExecute();

            // The abort the silence watchdog uses: disposing the client tears down the channel/socket,
            // which makes a parked synchronous PipeStream.Read return/throw so its thread-pool thread
            // unwinds instead of leaking. After this the loop throws TimeoutException → Disconnected.
            await PumpLinesAsync(reader, abort: client.Dispose, SilenceTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            // On the silence-timeout path abort() has already disposed the whole client, so the
            // command's channel may be torn down by the time we unwind here. SSH.NET's double-Dispose
            // behaviour is version-dependent, so dispose defensively: a throw while unwinding must
            // never mask the TimeoutException that drives Disconnected → reconnect — otherwise the
            // freeze this fix targets would silently return.
            SafeDispose(reader);
            SafeDispose(cmd);
        }
    }

    private static void SafeDispose(IDisposable disposable)
    {
        try { disposable.Dispose(); }
        catch { /* already torn down by abort(); never let teardown mask the connection outcome. */ }
    }

    /// <summary>
    /// Reads NDJSON lines from <paramref name="reader"/>, raising <see cref="MetricsReceived"/> per
    /// parsed sample, until EOF, cancellation, or <paramref name="silenceTimeout"/> of silence.
    ///
    /// The silence window (§5.4) is enforced with a REAL wall-clock timer raced against the read —
    /// NOT by cancelling the read. SSH.NET's <c>PipeStream.Read</c> is a blocking synchronous read
    /// reached via the base <c>Stream.ReadAsync</c> fallback, so <c>ReadLineAsync(token)</c> does not
    /// observe the token once a read is in flight; a silent-but-open channel would otherwise park it
    /// forever (the 4-hour freeze). On timeout we invoke <paramref name="abort"/> (dispose the client)
    /// to unwind that parked read, then throw <see cref="TimeoutException"/> so the caller reconnects.
    ///
    /// Internal seam (not for direct use): exposed so the watchdog can be tested against a read that
    /// never completes and ignores its token, with no real socket.
    /// </summary>
    internal async Task PumpLinesAsync(
        TextReader reader, Action abort, TimeSpan silenceTimeout, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var readTask = reader.ReadLineAsync(ct).AsTask();

            using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var timerTask = Task.Delay(silenceTimeout, timerCts.Token);

            var winner = await Task.WhenAny(readTask, timerTask).ConfigureAwait(false);

            if (winner != readTask)
            {
                // The timer (or ct) won — the read is still parked. Observe it so its eventual
                // failure (once aborted/torn down) is not an unobserved task exception.
                ObserveFault(readTask);

                if (ct.IsCancellationRequested)
                    break; // stop requested, not a silence timeout

                // Real silence: unwind the parked read by tearing down the connection, then report
                // the timeout so RunAsync transitions to Disconnected and reconnects (§5.4).
                _logger?.Log(LogSeverity.Warning, "silence watchdog fired", new Dictionary<string, object?>
                {
                    ["server_id"] = ServerId,
                    ["silence_sec"] = silenceTimeout.TotalSeconds,
                });
                abort();
                throw new TimeoutException(
                    $"no data for {silenceTimeout.TotalSeconds:0}s (wall-clock watchdog).");
            }

            // Read won: stop the timer (and observe its cancellation) before consuming the line.
            timerCts.Cancel();
            ObserveFault(timerTask);

            var line = await readTask.ConfigureAwait(false); // already completed; surfaces real errors
            if (line is null)
                return; // EOF

            if (line.Length == 0)
                continue;

            if (NdjsonParser.TryParse(line, out var sample) && sample is not null)
            {
                MetricsReceived?.Invoke(this, new MetricsReceivedEventArgs(sample));
                RecordHeartbeat();
            }
            // else: malformed line dropped (§4), keep reading.
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Summarises the received-data heartbeat at most once per <see cref="HeartbeatInterval"/>
    /// (instruction §6 Debug). The <see cref="IAppLogger.IsEnabled"/> guard means that in the default
    /// (OFF) mode this is a single comparison per sample with no allocation, so the 1 Hz stream stays
    /// within the low-load budget (§13.2).
    /// </summary>
    private void RecordHeartbeat()
    {
        if (_logger is null || !_logger.IsEnabled(LogSeverity.Debug))
            return;

        _samplesSinceHeartbeat++;
        var now = DateTime.UtcNow;
        if (_lastHeartbeatUtc == default)
        {
            _lastHeartbeatUtc = now;
            return;
        }

        if (now - _lastHeartbeatUtc < HeartbeatInterval)
            return;

        _logger.Log(LogSeverity.Debug, "heartbeat", new Dictionary<string, object?>
        {
            ["server_id"] = ServerId,
            ["samples"] = _samplesSinceHeartbeat,
        });
        _samplesSinceHeartbeat = 0;
        _lastHeartbeatUtc = now;
    }

    /// <summary>Drains a task's eventual fault/cancellation so an abandoned read/timer can't become
    /// an unobserved task exception. Fire-and-forget; we never need the result.</summary>
    private static void ObserveFault(Task task)
        => task.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static TimeSpan Backoff(int attempt)
    {
        // 1s, 2s, 4s, ... capped at 30s (exponential with a ceiling).
        double seconds = BackoffBase.TotalSeconds * Math.Pow(2, Math.Min(attempt, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, BackoffCap.TotalSeconds));
    }

    private void SetState(ConnectionState next, string? detail, Exception? error = null)
    {
        if (State == next)
            return;

        var old = State;
        State = next;
        LogTransition(old, next, detail, error);
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(old, next, detail));
    }

    /// <summary>
    /// Records the transition (instruction §6). Level by destination: Connected = Info,
    /// Disconnected = Warning, HostKeyMismatch = Critical (MITM suspicion, §6.1), Connecting = Debug.
    /// Fields carry only the non-secret <see cref="ServerId"/>, the from/to states and the (already
    /// secret-free) detail; any exception is reduced to its type name + (debug-only) stack by the
    /// logger — never its Message (§4).
    /// </summary>
    private void LogTransition(ConnectionState from, ConnectionState to, string? detail, Exception? error)
    {
        if (_logger is null)
            return;

        var severity = to switch
        {
            ConnectionState.Connected => LogSeverity.Info,
            ConnectionState.Disconnected => LogSeverity.Warning,
            ConnectionState.HostKeyMismatch => LogSeverity.Critical,
            _ => LogSeverity.Debug, // Connecting
        };

        var properties = new Dictionary<string, object?>
        {
            ["server_id"] = ServerId,
            ["from"] = from.ToString(),
            ["to"] = to.ToString(),
        };
        if (!string.IsNullOrEmpty(detail))
            properties["detail"] = detail;

        _logger.Log(severity, $"connection {to.ToString().ToLowerInvariant()}", properties, error);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        // MEDIUM 3: no shared PrivateKeyFile to dispose here — each connection owns and disposes its
        // own credential inside the reconnect loop.
    }
}
