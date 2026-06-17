using VpsWatcher.Core;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Schema;
using VpsWatcher.Core.Ssh;

// Two modes:
//
//   (default) stdin  — pipe NDJSON in, parse with the Core parser, print a summary (Phase 1):
//       ssh -i <key> -p 49222 metrics@<host> | dotnet run --project VpsWatcher.ConsoleTest
//
//   --ssh            — connect in-process via SSH.NET, verify the host key, stream NDJSON (Phase 2).
//       Connection details come from CLI args OR env vars and are NEVER committed:
//         --ssh --host H --port P --user U --key <path> --knownhostkey "SHA256:..."
//       env equivalents: VPSWATCH_HOST VPSWATCH_PORT VPSWATCH_USER VPSWATCH_KEYPATH VPSWATCH_KNOWNHOSTKEY
//
//     Try it both ways:
//       (1) correct knownhostkey -> state Connecting->Connected, NDJSON flows (first frame null -> real values).
//       (2) wrong   knownhostkey -> state Connecting->HostKeyMismatch, NO reconnect (MITM refused).

var argMap = ParseArgs(args);

if (argMap.ContainsKey("ssh"))
{
    await RunSshAsync(argMap);
    return;
}

RunStdin();

// ───────────────────────── modes ─────────────────────────

static void RunStdin()
{
    Console.Error.WriteLine("[VpsWatcher.ConsoleTest] reading NDJSON from stdin (Ctrl+C to stop)...");

    int ok = 0, bad = 0;
    string? line;
    while ((line = Console.ReadLine()) is not null)
    {
        if (line.Trim().Length == 0)
            continue;

        if (NdjsonParser.TryParse(line, out var s) && s is not null)
        {
            ok++;
            PrintSample(s);
        }
        else
        {
            bad++;
            Console.Error.WriteLine($"[skipped malformed line] {line}");
        }
    }

    Console.Error.WriteLine($"[VpsWatcher.ConsoleTest] done. parsed={ok} skipped={bad}");
}

static async Task RunSshAsync(IReadOnlyDictionary<string, string> a)
{
    var config = new ServerConfig
    {
        Id = Optional(a, "id", "VPSWATCH_ID") ?? "manual-test",
        Host = Required(a, "host", "VPSWATCH_HOST"),
        Port = int.Parse(Required(a, "port", "VPSWATCH_PORT")),
        User = Required(a, "user", "VPSWATCH_USER"),
        KeyPath = Required(a, "key", "VPSWATCH_KEYPATH"),
        KnownHostKey = Required(a, "knownhostkey", "VPSWATCH_KNOWNHOSTKEY"),
    };

    // Note: we intentionally print the server *id* (not the host/IP) so logs stay shareable.
    using var svc = new SshConnectionService(config);

    svc.StateChanged += (_, e) =>
        Console.Error.WriteLine($"[state] {e.OldState} -> {e.NewState}"
            + (e.Detail is null ? "" : $"  ({e.Detail})"));

    svc.MetricsReceived += (_, e) => PrintSample(e.Sample);

    var stop = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };

    Console.Error.WriteLine($"[ssh] starting connection to id={config.Id} (Ctrl+C to stop)...");
    svc.Start();
    await stop.Task;
    await svc.StopAsync();
    Console.Error.WriteLine("[ssh] stopped.");
}

// ───────────────────────── helpers ─────────────────────────

static void PrintSample(Sample s)
{
    // null rates print as "--" so "measuring" stays visually distinct from 0 (§3).
    string cpu = s.CpuPct is { } c ? $"{c,5:0.0}%" : "  -- ";
    string rx = s.Net.RxBps is { } r ? $"{r,9:0}" : "       --";
    string tx = s.Net.TxBps is { } t ? $"{t,9:0}" : "       --";
    string disks = string.Join(",", s.Disk.Select(d => $"{d.Mount}={d.UsedPct:0.0}%"));
    Console.WriteLine(
        $"id={s.Id} ts={s.Ts} cpu={cpu} mem={s.Mem.UsedPct,5:0.0}% " +
        $"rx={rx} tx={tx} disk[{disks}] load={string.Join("/", s.Load)}");
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;

        string key = args[i][2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            map[key] = args[++i];
        }
        else
        {
            map[key] = "true";
        }
    }
    return map;
}

static string? Optional(IReadOnlyDictionary<string, string> a, string argKey, string envKey)
{
    if (a.TryGetValue(argKey, out var v) && !string.IsNullOrWhiteSpace(v))
        return v;
    var e = Environment.GetEnvironmentVariable(envKey);
    return string.IsNullOrWhiteSpace(e) ? null : e;
}

static string Required(IReadOnlyDictionary<string, string> a, string argKey, string envKey)
    => Optional(a, argKey, envKey)
       ?? throw new InvalidOperationException(
           $"missing required setting: pass --{argKey} <value> or set {envKey} " +
           "(real connection values must never be committed).");
