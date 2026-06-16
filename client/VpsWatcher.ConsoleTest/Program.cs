using VpsWatcher.Core;

// Phase 1 (no UI, no SSH yet): read NDJSON lines from stdin, parse them with the
// shared Core parser, and print a one-line summary per sample. This lets us verify
// the parser against live data by piping the agent's stream into it, e.g.:
//
//     ssh -i <key> -p 49222 metrics@<host> | dotnet run --project VpsWatcher.ConsoleTest
//
// The real in-process SSH connection service (SSH.NET + host-key pinning) is Phase 2,
// which requires dependency approval and a critical-security-reviewer pass (CLAUDE.md).

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
        // null rates print as "--" to keep "measuring" visually distinct from 0 (§3).
        string cpu = s.CpuPct is { } c ? $"{c,5:0.0}%" : "  -- ";
        string rx = s.Net.RxBps is { } r ? $"{r,9:0}" : "       --";
        string tx = s.Net.TxBps is { } t ? $"{t,9:0}" : "       --";
        string disks = string.Join(",", s.Disk.Select(d => $"{d.Mount}={d.UsedPct:0.0}%"));
        Console.WriteLine(
            $"id={s.Id} ts={s.Ts} cpu={cpu} mem={s.Mem.UsedPct,5:0.0}% " +
            $"rx={rx} tx={tx} disk[{disks}] load={string.Join("/", s.Load)}");
    }
    else
    {
        bad++;
        Console.Error.WriteLine($"[skipped malformed line] {line}");
    }
}

Console.Error.WriteLine($"[VpsWatcher.ConsoleTest] done. parsed={ok} skipped={bad}");
