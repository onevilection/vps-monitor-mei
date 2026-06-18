using System.IO;
using System.Text.Json;
using VpsWatcher.Core.Configuration;

namespace VpsWatcher.App.Configuration;

/// <summary>
/// Loads the (single, Phase 3a) server connection target WITHOUT ever reading repo-committed
/// secrets. Resolution order, highest first:
/// <list type="number">
///   <item>CLI args / env vars — the same keys ConsoleTest used in Phase 2
///         (<c>--host/--port/--user/--key/--knownhostkey</c> · <c>VPSWATCH_*</c>).</item>
///   <item><c>%APPDATA%\VpsWatcher\servers.json</c> (gitignored) — first entry.</item>
/// </list>
/// Real host/key/fingerprint live only in those user-local places, never in the repo (CLAUDE.md).
/// </summary>
public static class AppServerConfigLoader
{
    /// <summary>
    /// Returns the connection target, or <c>null</c> with <paramref name="error"/> set when nothing
    /// is configured / the file is unreadable. Never throws on absence — the UI shows the message.
    /// </summary>
    public static ServerConfig? Load(string[] args, out string? error)
    {
        error = null;
        var argMap = ParseArgs(args);

        // 1) explicit args / env vars take precedence.
        var host = Value(argMap, "host", "VPSWATCH_HOST");
        if (!string.IsNullOrWhiteSpace(host))
        {
            var portText = Value(argMap, "port", "VPSWATCH_PORT");
            if (!int.TryParse(portText, out var port))
            {
                error = $"接続設定エラー: port が不正です（'{portText}'）。";
                return null;
            }

            return new ServerConfig
            {
                Id = Value(argMap, "id", "VPSWATCH_ID") ?? "manual-test",
                Label = Value(argMap, "label", "VPSWATCH_LABEL"),
                Host = host!,
                Port = port,
                User = Value(argMap, "user", "VPSWATCH_USER") ?? "",
                KeyPath = Value(argMap, "key", "VPSWATCH_KEYPATH") ?? "",
                KnownHostKey = Value(argMap, "knownhostkey", "VPSWATCH_KNOWNHOSTKEY") ?? "",
            };
        }

        // 2) %APPDATA%\VpsWatcher\servers.json (user-local, gitignored).
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VpsWatcher", "servers.json");

        return LoadFromServersJson(path, out error);
    }

    /// <summary>
    /// Loads the first server from a servers.json at <paramref name="path"/>. Internal seam so the
    /// parse-error branch is unit-testable against a temp file — the public <see cref="Load"/> always
    /// uses the user-local %APPDATA% path, which tests must never read or overwrite.
    /// </summary>
    internal static ServerConfig? LoadFromServersJson(string path, out string? error)
    {
        error = null;

        if (!File.Exists(path))
        {
            // Not-yet-configured: the path here is a location hint (where to create the file), not
            // file contents, so showing it is intentional and helpful.
            error =
                "接続先が未設定です。環境変数 VPSWATCH_HOST/PORT/USER/KEYPATH/KNOWNHOSTKEY を設定するか、" +
                $"{path} に servers.json（servers.example.json 参照）を置いてください。";
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var servers = JsonSerializer.Deserialize<List<ServerConfig>>(json, JsonOptions);
            if (servers is null || servers.Count == 0)
            {
                error = $"servers.json にサーバ定義がありません: {path}";
                return null;
            }

            // Phase 3a: first server only. N-server support is Phase 3b.
            return servers[0];
        }
        catch (JsonException ex)
        {
            // servers.json holds host/key/fingerprint; JsonException.Message can echo offending
            // fragments, and the file's full path embeds the OS username. StatusMessage is painted
            // on the transparent gadget (screenshot/screen-share surface), so keep BOTH the raw
            // parser message and the path off-screen — show only a fixed message and send the detail
            // (message + path) to Trace (off-screen, no persistent log). Same discipline as MEDIUM 1.
            System.Diagnostics.Trace.TraceError($"failed to parse servers.json at '{path}': {ex}");
            error = "servers.json の解析に失敗しました。詳細はデバッグ出力を参照してください。";
            return null;
        }
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
