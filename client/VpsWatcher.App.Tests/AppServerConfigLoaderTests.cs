using System;
using System.IO;
using VpsWatcher.App.Configuration;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Tests for <see cref="AppServerConfigLoader"/>'s servers.json parse path (design / secrets).
/// servers.json holds host/key/fingerprint, and the surfaced error becomes the on-screen
/// StatusMessage on the transparent gadget — so a malformed file must not leak the parser's raw
/// message, secret fragments, or the file's full path. Uses a temp file; never the real %APPDATA%.
/// </summary>
public sealed class AppServerConfigLoaderTests
{
    [Fact]
    public void Malformed_servers_json_does_not_leak_parse_detail_or_path_to_error()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vpswatcher_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "servers.json");
        // Malformed JSON whose tokens carry secret-looking fragments (IP / key path), then truncated
        // so System.Text.Json throws.
        File.WriteAllText(path, "{ \"host\": \"203.0.113.77\", \"keyPath\": \"C:\\\\secret\\\\my_key\" ");
        try
        {
            var config = AppServerConfigLoader.LoadFromServersJson(path, out var error);

            Assert.Null(config);
            Assert.NotNull(error);
            // Fixed template only — no raw parser message, no secret fragments, no full path.
            Assert.Equal("servers.json の解析に失敗しました。詳細はデバッグ出力を参照してください。", error);
            Assert.DoesNotContain("203.0.113.77", error!);
            Assert.DoesNotContain("my_key", error!);
            Assert.DoesNotContain(path, error!);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
