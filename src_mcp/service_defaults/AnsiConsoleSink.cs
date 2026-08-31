using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace CoaiMcp.ServiceDefaults;

/// <summary>
/// The coloured console sink — ours, because the packaged themes do not survive redirection.
/// </summary>
/// <remarks>
/// <para>Measured on Serilog.Sinks.Console 6.1.1 (2026-08, the family logging rule): with stdout
/// redirected, <c>AnsiConsoleTheme.Code</c> + <c>applyThemeToRedirectedOutput: true</c> emits
/// <b>zero</b> escape bytes — and an orchestrator capturing a child's output redirects by
/// definition, so the theme produces colour exactly where nobody is looking. This sink writes the
/// escapes unconditionally.</para>
/// <para>Only the level is coloured strongly: a line where everything is coloured is a line where
/// nothing stands out. The writer is injected so a test can read the bytes.</para>
/// </remarks>
public sealed class AnsiConsoleSink(ITextFormatter formatter, TextWriter writer) : ILogEventSink
{
    private const string Reset = "\x1b[0m";

    public void Emit(LogEvent logEvent)
    {
        var line = new StringWriter();
        formatter.Format(logEvent, line);
        var text = line.ToString();

        // Colour the level token in place: it is rendered as "] LVL" nowhere and "[HH:mm:ss LVL]"
        // exactly once, so replacing the first occurrence is deterministic.
        var token = CoaiTextFormatter.LevelToken(logEvent.Level);
        var colour = Colour(logEvent.Level);
        var index = text.IndexOf(token, StringComparison.Ordinal);
        writer.Write(index < 0 ? text : $"{text[..index]}{colour}{token}{Reset}{text[(index + token.Length)..]}");
        writer.Flush();
    }

    internal static string Colour(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "\x1b[90m",   // dim gray
        LogEventLevel.Debug => "\x1b[37m",     // gray
        LogEventLevel.Information => "\x1b[32m", // green
        LogEventLevel.Warning => "\x1b[33m",   // yellow
        LogEventLevel.Error => "\x1b[31m",     // red
        _ => "\x1b[97;41m",                    // white on red
    };
}
