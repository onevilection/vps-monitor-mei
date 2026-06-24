using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VpsWatcher.Core.Logging;

namespace VpsWatcher.Core.Tests;

/// <summary>
/// Tests for the generic NDJSON logging layer (instruction §1/§3/§4/§7). The layer is app-agnostic
/// (app name + log directory injected); these tests exercise the real file path written to a temp
/// directory so they never touch %APPDATA%.
/// </summary>
public class AppLoggerTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vpswatcher_log_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string AllText(string dir)
        => string.Join("\n", Directory.GetFiles(dir, "*.ndjson").SelectMany(File.ReadAllLines));

    /// <summary>Reads every NDJSON line back as a parsed JSON document — also asserting that each
    /// line is exactly one JSON object (1 line = 1 record, jq-parseable, §3).</summary>
    private static List<JsonDocument> ReadRecords(string dir)
    {
        var docs = new List<JsonDocument>();
        foreach (var file in Directory.GetFiles(dir, "*.ndjson"))
            foreach (var line in File.ReadAllLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                docs.Add(JsonDocument.Parse(line)); // throws if a line is not a single JSON object
            }
        return docs;
    }

    [Fact]
    public void Off_mode_records_info_and_above_but_drops_debug()
    {
        var dir = NewTempDir();
        try
        {
            using (var log = AppLogger.CreateFile("VpsWatcher", debugMode: false, dir))
            {
                log.Log(LogSeverity.Debug, "debug-msg");
                log.Log(LogSeverity.Info, "info-msg");
                log.Log(LogSeverity.Warning, "warn-msg");
            }

            var msgs = ReadRecords(dir)
                .Select(d => d.RootElement.GetProperty("message").GetString())
                .ToList();

            Assert.DoesNotContain("debug-msg", msgs); // OFF threshold = Info+
            Assert.Contains("info-msg", msgs);
            Assert.Contains("warn-msg", msgs);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Debug_mode_records_debug_and_above()
    {
        var dir = NewTempDir();
        try
        {
            using (var log = AppLogger.CreateFile("VpsWatcher", debugMode: true, dir))
                log.Log(LogSeverity.Debug, "debug-msg");

            var msgs = ReadRecords(dir)
                .Select(d => d.RootElement.GetProperty("message").GetString())
                .ToList();

            Assert.Contains("debug-msg", msgs); // ON threshold = Debug+
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsEnabled_reflects_the_debug_mode_threshold()
    {
        var dir = NewTempDir();
        try
        {
            using var off = AppLogger.CreateFile("VpsWatcher", debugMode: false, dir);
            Assert.False(off.IsEnabled(LogSeverity.Debug));
            Assert.True(off.IsEnabled(LogSeverity.Info));

            using var on = AppLogger.CreateFile("VpsWatcher", debugMode: true, dir);
            Assert.True(on.IsEnabled(LogSeverity.Debug));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Each_record_is_one_json_object_with_core_fields()
    {
        var dir = NewTempDir();
        try
        {
            using (var log = AppLogger.CreateFile("VpsWatcher", debugMode: false, dir))
                log.Log(LogSeverity.Info, "connected",
                    new Dictionary<string, object?> { ["server_id"] = "rasiiku_claude" });

            var rec = Assert.Single(ReadRecords(dir));
            var root = rec.RootElement;

            Assert.Equal("VpsWatcher", root.GetProperty("app").GetString());
            Assert.Equal("Info", root.GetProperty("level").GetString());
            Assert.Equal("connected", root.GetProperty("message").GetString());
            // structured property surfaces as an independent top-level field (jq 'select(.server_id==...)')
            Assert.Equal("rasiiku_claude", root.GetProperty("server_id").GetString());
            Assert.True(root.TryGetProperty("timestamp", out _));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Five_levels_normalize_to_the_expected_names()
    {
        var dir = NewTempDir();
        try
        {
            using (var log = AppLogger.CreateFile("VpsWatcher", debugMode: true, dir))
            {
                log.Log(LogSeverity.Debug, "d");
                log.Log(LogSeverity.Info, "i");
                log.Log(LogSeverity.Warning, "w");
                log.Log(LogSeverity.Error, "e");
                log.Log(LogSeverity.Critical, "c");
            }

            var byMsg = ReadRecords(dir).ToDictionary(
                d => d.RootElement.GetProperty("message").GetString()!,
                d => d.RootElement.GetProperty("level").GetString());

            Assert.Equal("Debug", byMsg["d"]);
            Assert.Equal("Info", byMsg["i"]);
            Assert.Equal("Warning", byMsg["w"]);
            Assert.Equal("Error", byMsg["e"]);
            Assert.Equal("Critical", byMsg["c"]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Exception_logs_the_type_name_but_never_the_exception_message()
    {
        // §4 (MEDIUM 1 延長): SSH.NET exceptions embed the key path / host in ex.Message. The logger
        // must record only ex.GetType().Name (as `reason`) and never the message — even when the
        // caller passes the exception object directly.
        var dir = NewTempDir();
        const string secretHost = "203.0.113.10";
        const string secretKeyPath = @"C:\Users\me\.ssh\watcher_ed25519";
        try
        {
            using (var log = AppLogger.CreateFile("VpsWatcher", debugMode: false, dir))
            {
                var ex = new InvalidOperationException($"connect to {secretHost} key {secretKeyPath} failed");
                log.Log(LogSeverity.Error, "connection init failed",
                    new Dictionary<string, object?> { ["server_id"] = "rasiiku_claude" }, ex);
            }

            var text = AllText(dir);
            Assert.Contains("InvalidOperationException", text); // type name is allowed
            Assert.DoesNotContain(secretHost, text);            // ex.Message content must not leak
            Assert.DoesNotContain(secretKeyPath, text);

            var rec = Assert.Single(ReadRecords(dir));
            Assert.Equal("InvalidOperationException", rec.RootElement.GetProperty("reason").GetString());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Stack_trace_is_omitted_off_and_present_on_but_never_carries_the_message()
    {
        // §4: a stack trace may be recorded in debug_mode, but ex.Message must be excluded even then;
        // OFF mode records no stack trace at all (keeps the file lean / §13.2 spirit).
        const string secretMessage = "secret-message-abc123";

        Exception Make()
        {
            try { throw new InvalidOperationException(secretMessage); }
            catch (Exception e) { return e; }
        }

        var off = NewTempDir();
        try
        {
            using (var log = AppLogger.CreateFile("VpsWatcher", debugMode: false, off))
                log.Log(LogSeverity.Error, "boom", null, Make());

            var rec = Assert.Single(ReadRecords(off));
            Assert.False(rec.RootElement.TryGetProperty("stack_trace", out _));
            Assert.DoesNotContain(secretMessage, AllText(off));
        }
        finally { Directory.Delete(off, recursive: true); }

        var on = NewTempDir();
        try
        {
            using (var log = AppLogger.CreateFile("VpsWatcher", debugMode: true, on))
                log.Log(LogSeverity.Error, "boom", null, Make());

            var rec = Assert.Single(ReadRecords(on));
            Assert.True(rec.RootElement.TryGetProperty("stack_trace", out var st));
            var stackText = st.GetString()!;
            Assert.Contains(nameof(Stack_trace_is_omitted_off_and_present_on_but_never_carries_the_message), stackText);
            Assert.DoesNotContain(secretMessage, stackText); // still no message in debug
        }
        finally { Directory.Delete(on, recursive: true); }
    }

    [Fact]
    public void Non_ascii_text_is_written_literally_not_unicode_escaped()
    {
        // A local debug log is read by humans and jq, so Japanese / em-dash / '+' in the offset should
        // appear literally (relaxed escaping) rather than as \uXXXX. Both parse the same, but literal
        // is far more readable when grepping the file.
        var dir = NewTempDir();
        try
        {
            using (var log = AppLogger.CreateFile("VpsWatcher", debugMode: false, dir))
                log.Log(LogSeverity.Warning, "接続が切れました",
                    new Dictionary<string, object?> { ["detail"] = "ホスト鍵不一致 — 停止" });

            var text = AllText(dir);
            Assert.Contains("接続が切れました", text);
            Assert.Contains("ホスト鍵不一致 — 停止", text);
            Assert.DoesNotContain("\\u", text); // nothing got unicode-escaped
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Logging_never_throws_when_the_directory_cannot_be_created()
    {
        // §4: a logging failure must never disrupt the app. An unusable directory falls back to a
        // no-op logger rather than throwing.
        var bogus = Path.Combine(Path.GetTempPath(), "vpswatcher_file_as_dir_" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(bogus, "x"); // a FILE where a directory path is expected
        try
        {
            using var log = AppLogger.CreateFile("VpsWatcher", debugMode: false, bogus);
            var ex = Record.Exception(() => log.Log(LogSeverity.Error, "still-alive"));
            Assert.Null(ex);
        }
        finally { File.Delete(bogus); }
    }
}
