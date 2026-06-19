using System;
using System.IO;
using System.Linq;
using VpsWatcher.App.Configuration;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Tests for <see cref="AppServerConfigLoader"/>'s servers.json (array) path — N-server loading and
/// fail-soft exclusion (design §5.3/§9.1/§5.4.1, secrets / MEDIUM 1). servers.json holds
/// host/key/fingerprint, and the surfaced error becomes the on-screen StatusMessage on the
/// transparent gadget — so a malformed file (or an all-unusable file) must not leak the parser's raw
/// message, secret fragments, or the file's full path. Uses temp files; never the real %APPDATA%.
/// </summary>
public sealed class AppServerConfigLoaderTests
{
    private const string ValidPin = "SHA256:D5cKV/20wphMD2QliSjc7EQgc1fkk5FRHFgq0XUD5GQ";

    private static string WriteTemp(string contents, out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "vpswatcher_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "servers.json");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Array_with_multiple_servers_loads_all_in_order()
    {
        var path = WriteTemp(
            $$"""
            [
              { "id": "vps-a", "host": "203.0.113.10", "port": 49222, "user": "metrics",
                "keyPath": "C:/k/a", "knownHostKey": "{{ValidPin}}" },
              { "id": "vps-b", "host": "203.0.113.20", "port": 49222, "user": "metrics",
                "keyPath": "C:/k/b", "knownHostKey": "{{ValidPin}}" }
            ]
            """, out var dir);
        try
        {
            var configs = AppServerConfigLoader.LoadFromServersJson(path, out var error);

            Assert.Null(error);
            Assert.Equal(2, configs.Count);
            Assert.Equal("vps-a", configs[0].Id); // array order preserved (§5.3)
            Assert.Equal("vps-b", configs[1].Id);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Entry_with_empty_known_host_key_is_dropped_but_others_still_load()
    {
        // Fail-CLOSED per server (no pin ⇒ no MITM detection ⇒ excluded), fail-SOFT overall: the
        // correctly-pinned server still loads. One mis-configured entry must not sink the rest.
        var path = WriteTemp(
            $$"""
            [
              { "id": "vps-nopin", "host": "203.0.113.10", "port": 49222, "user": "metrics",
                "keyPath": "C:/k/a", "knownHostKey": "" },
              { "id": "vps-ok", "host": "203.0.113.20", "port": 49222, "user": "metrics",
                "keyPath": "C:/k/b", "knownHostKey": "{{ValidPin}}" }
            ]
            """, out var dir);
        try
        {
            var configs = AppServerConfigLoader.LoadFromServersJson(path, out var error);

            Assert.Null(error);                               // a usable server remains ⇒ no empty state
            Assert.Single(configs);
            Assert.Equal("vps-ok", configs[0].Id);
            Assert.DoesNotContain(configs, c => c.Id == "vps-nopin");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void All_entries_unusable_yields_empty_list_and_fixed_secret_free_error()
    {
        // Every entry lacks a pin: nothing to show. The error must be a fixed template — no host,
        // no key path, no per-entry detail on the transparent gadget (MEDIUM 1).
        var path = WriteTemp(
            """
            [
              { "id": "vps-1", "host": "203.0.113.77", "port": 49222, "user": "metrics",
                "keyPath": "C:/secret/my_key", "knownHostKey": "" }
            ]
            """, out var dir);
        try
        {
            var configs = AppServerConfigLoader.LoadFromServersJson(path, out var error);

            Assert.Empty(configs);
            Assert.NotNull(error);
            Assert.DoesNotContain("203.0.113.77", error!);
            Assert.DoesNotContain("my_key", error!);
            Assert.DoesNotContain(path, error!);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Malformed_servers_json_does_not_leak_parse_detail_or_path_to_error()
    {
        // Malformed JSON whose tokens carry secret-looking fragments (IP / key path), truncated so
        // System.Text.Json throws.
        var path = WriteTemp(
            "{ \"host\": \"203.0.113.77\", \"keyPath\": \"C:\\\\secret\\\\my_key\" ", out var dir);
        try
        {
            var configs = AppServerConfigLoader.LoadFromServersJson(path, out var error);

            Assert.Empty(configs);
            Assert.NotNull(error);
            // Fixed template only — no raw parser message, no secret fragments, no full path.
            Assert.Equal("servers.json の解析に失敗しました。詳細はデバッグ出力を参照してください。", error);
            Assert.DoesNotContain("203.0.113.77", error!);
            Assert.DoesNotContain("my_key", error!);
            Assert.DoesNotContain(path, error!);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Repo_servers_example_json_parses_as_two_servers()
    {
        // The shipped template must stay valid and demonstrate the N-server (array) shape. Its dummy
        // knownHostKey ("REPLACE_WITH_...") is non-empty, so neither entry is filtered.
        var examplePath = FindRepoFile("servers.example.json");

        var configs = AppServerConfigLoader.LoadFromServersJson(examplePath, out var error);

        Assert.Null(error);
        Assert.Equal(2, configs.Count);
    }

    /// <summary>Walks up from the test assembly's directory to find a repo-root file.</summary>
    private static string FindRepoFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException($"could not locate '{fileName}' walking up from the test bin dir.");
    }
}
