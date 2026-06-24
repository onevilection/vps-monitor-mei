using System.IO;
using System.Text.Json;
using VpsWatcher.Core.Configuration;

namespace VpsWatcher.App.Configuration;

/// <summary>
/// Loads the N server connection targets WITHOUT ever reading repo-committed secrets.
/// Resolution order, highest first:
/// <list type="number">
///   <item>CLI args / env vars — a single server, for back-compat with Phase 2 ConsoleTest
///         (<c>--host/--port/--user/--key/--knownhostkey</c> · <c>VPSWATCH_*</c>).</item>
///   <item><c>%APPDATA%\VpsWatcher\servers.json</c> (gitignored) — the full array (Phase 4).</item>
/// </list>
/// Real host/key/fingerprint live only in those user-local places, never in the repo (CLAUDE.md).
///
/// Fail-closed per server, fail-soft overall (§5.4.1/§9.1): an entry without a pinned
/// <c>knownHostKey</c> is dropped (MITM detection can't hold without a pin) with the reason sent to
/// Trace — but the other correctly-pinned servers still load, so one mis-configured entry never
/// takes down the whole gadget. On-screen <paramref name="error"/> text stays a fixed template with
/// no parser detail, no path and no secret fragments (MEDIUM 1).
/// </summary>
public static class AppServerConfigLoader
{
    /// <summary>
    /// Returns the connection targets (possibly empty). When the list is empty, <paramref name="error"/>
    /// carries a fixed, secret-free message for the empty-state UI. Never throws on absence.
    /// </summary>
    public static IReadOnlyList<ServerConfig> Load(string[] args, out string? error)
    {
        error = null;
        var argMap = ParseArgs(args);

        // 1) explicit args / env vars take precedence — a single server (back-compat).
        var host = Value(argMap, "host", "VPSWATCH_HOST");
        if (!string.IsNullOrWhiteSpace(host))
        {
            var portText = Value(argMap, "port", "VPSWATCH_PORT");
            if (!int.TryParse(portText, out var port))
            {
                error = $"接続設定エラー: port が不正です（'{portText}'）。";
                return Array.Empty<ServerConfig>();
            }

            var cfg = new ServerConfig
            {
                Id = Value(argMap, "id", "VPSWATCH_ID") ?? "manual-test",
                Label = Value(argMap, "label", "VPSWATCH_LABEL"),
                Host = host!,
                Port = port,
                User = Value(argMap, "user", "VPSWATCH_USER") ?? "",
                KeyPath = Value(argMap, "key", "VPSWATCH_KEYPATH") ?? "",
                KnownHostKey = Value(argMap, "knownhostkey", "VPSWATCH_KNOWNHOSTKEY") ?? "",
            };
            return FilterUsable(new[] { cfg }, out error);
        }

        // 2) %APPDATA%\VpsWatcher\servers.json (user-local, gitignored) — the full array.
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VpsWatcher", "servers.json");

        return LoadFromServersJson(path, out error);
    }

    /// <summary>
    /// Loads every server from a servers.json at <paramref name="path"/>. Internal seam so the
    /// array/parse/fail-soft branches are unit-testable against a temp file — the public
    /// <see cref="Load"/> always uses the user-local %APPDATA% path, which tests must never touch.
    /// </summary>
    internal static IReadOnlyList<ServerConfig> LoadFromServersJson(string path, out string? error)
    {
        error = null;

        if (!File.Exists(path))
        {
            // Not-yet-configured: the path here is a location hint (where to create the file), not
            // file contents, so showing it is intentional and helpful.
            error =
                "接続先が未設定です。環境変数 VPSWATCH_HOST/PORT/USER/KEYPATH/KNOWNHOSTKEY を設定するか、" +
                $"{path} に servers.json（servers.example.json 参照）を置いてください。";
            return Array.Empty<ServerConfig>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var servers = JsonSerializer.Deserialize<List<ServerConfig>>(json, JsonOptions);
            if (servers is null || servers.Count == 0)
            {
                error = "servers.json にサーバ定義がありません。servers.example.json を参照してください。";
                return Array.Empty<ServerConfig>();
            }

            return FilterUsable(servers, out error);
        }
        catch (JsonException ex)
        {
            // servers.json holds host/key/fingerprint; JsonException.Message can echo offending
            // fragments, and the file's full path embeds the OS username. The error becomes the
            // on-screen StatusMessage on the transparent gadget (screenshot/screen-share surface), and
            // Trace is forwarded to any listener (DebugView / ETW) — so keep the raw parser message,
            // {ex}, AND the path out of both: show a fixed message on-screen and send only the
            // exception TYPE name to Trace (security review HIGH / MEDIUM 1).
            System.Diagnostics.Trace.TraceError(
                $"failed to parse servers.json: {ex.GetType().Name}");
            error = "servers.json の解析に失敗しました。詳細はデバッグ出力を参照してください。";
            return Array.Empty<ServerConfig>();
        }
    }

    /// <summary>
    /// Drops entries with an empty <c>knownHostKey</c> (fail-closed per server: no pin ⇒ no MITM
    /// detection ⇒ not a connection target), keeping the correctly-pinned ones (fail-soft overall).
    /// The exclusion reason (server id only — never the host/key/path) goes to Trace. When EVERY
    /// entry is unusable, <paramref name="error"/> gets a fixed, secret-free message for the empty state.
    /// </summary>
    private static IReadOnlyList<ServerConfig> FilterUsable(
        IReadOnlyList<ServerConfig> servers, out string? error)
    {
        error = null;

        var usable = new List<ServerConfig>(servers.Count);
        int excluded = 0;
        foreach (var s in servers)
        {
            if (string.IsNullOrWhiteSpace(s.KnownHostKey))
            {
                excluded++;
                // Id and fingerprints aren't secret, but keep host/key/path out of even Trace — the
                // id alone is enough to point the user at the offending servers.json entry.
                System.Diagnostics.Trace.TraceWarning(
                    $"servers.json: excluding server '{s.Id}' — knownHostKey is empty " +
                    "(fail-closed; a pinned host key is required for MITM detection, §5.4.1/§9.1).");
                continue;
            }
            usable.Add(s);
        }

        if (usable.Count == 0)
        {
            // Every entry was unusable. Fixed template only — no parser detail, no path, no secret.
            error = excluded > 0
                ? "接続可能なサーバがありません（knownHostKey 未設定）。servers.json を確認してください。"
                : "servers.json にサーバ定義がありません。servers.example.json を参照してください。";
        }

        return usable;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;

            var key = args[i][2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                map[key] = args[++i];
            else
                map[key] = "true";
        }
        return map;
    }

    private static string? Value(IReadOnlyDictionary<string, string> a, string argKey, string envKey)
    {
        if (a.TryGetValue(argKey, out var v) && !string.IsNullOrWhiteSpace(v))
            return v;
        var e = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrWhiteSpace(e) ? null : e;
    }
}
