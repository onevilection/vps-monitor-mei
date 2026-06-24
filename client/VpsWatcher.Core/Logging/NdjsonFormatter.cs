using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace VpsWatcher.Core.Logging;

/// <summary>
/// Renders a Serilog <see cref="LogEvent"/> as a single NDJSON line (instruction §3): one JSON object
/// per line, jq-parseable. Fields are emitted in a fixed, readable order —
/// <c>timestamp</c> (ISO-8601 with local offset), <c>level</c> (normalised 5-level name), <c>app</c>,
/// <c>message</c> — followed by every structured property as its own top-level field. Generic: the
/// formatter has no VpsWatcher-specific knowledge.
/// </summary>
public sealed class NdjsonFormatter : ITextFormatter
{
    // Relaxed escaping so Japanese, em-dash and the '+' in the timezone offset are written literally
    // (human/jq readable in a local debug log) rather than as \uXXXX. This is a log file, never HTML,
    // so the relaxed encoder's caveats don't apply. Backslash/quote are still escaped (valid JSON).
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();

            // ISO-8601 with the local UTC offset (round-trippable). LogEvent.Timestamp is a
            // DateTimeOffset captured at the local offset by Serilog.
            writer.WriteString("timestamp", logEvent.Timestamp.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteString("level", NormaliseLevel(logEvent.Level));

            // `app` is injected as an enriched property; surface it as a defined field, then skip it
            // when iterating the remaining properties.
            string app = string.Empty;
            if (logEvent.Properties.TryGetValue("app", out var appValue)
                && appValue is ScalarValue { Value: string appName })
            {
                app = appName;
            }
            writer.WriteString("app", app);

            writer.WriteString("message", logEvent.RenderMessage(CultureInfo.InvariantCulture));

            foreach (var property in logEvent.Properties)
            {
                if (property.Key == "app")
                    continue;
                WriteProperty(writer, property.Key, property.Value);
            }

            writer.WriteEndObject();
        }

        output.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
        output.Write('\n');
    }

    /// <summary>Serilog's six levels → the five ov-logger severities (instruction §3).</summary>
    private static string NormaliseLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "Debug",
        LogEventLevel.Debug => "Debug",
        LogEventLevel.Information => "Info",
        LogEventLevel.Warning => "Warning",
        LogEventLevel.Error => "Error",
        LogEventLevel.Fatal => "Critical",
        _ => level.ToString(),
    };

    private static void WriteProperty(Utf8JsonWriter writer, string name, LogEventPropertyValue value)
    {
        if (value is ScalarValue scalar)
        {
            switch (scalar.Value)
            {
                case null:
                    writer.WriteNull(name);
                    return;
                case bool b:
                    writer.WriteBoolean(name, b);
                    return;
                case int i:
                    writer.WriteNumber(name, i);
                    return;
                case long l:
                    writer.WriteNumber(name, l);
                    return;
                case double d:
                    writer.WriteNumber(name, d);
                    return;
                case decimal m:
                    writer.WriteNumber(name, m);
                    return;
                case string s:
                    writer.WriteString(name, s);
                    return;
                default:
                    writer.WriteString(name, Convert.ToString(scalar.Value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        // Non-scalar (sequence/structure) — not used by our call sites, but stay robust: render its
        // text form rather than risk an unhandled shape.
        writer.WriteString(name, value.ToString());
    }
}
