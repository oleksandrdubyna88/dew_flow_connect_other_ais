using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace CoaiMcp.ServiceDefaults;

/// <summary>
/// Renders one log line: <c>[HH:mm:ss LVL] app#pid Context: message {extras}</c> — UTC, always.
/// </summary>
/// <remarks>
/// <para>The message goes through Serilog's own <see cref="MessageTemplateTextFormatter"/> with
/// <c>{Message:lj}</c>, never <c>LogEvent.RenderMessage()</c> — the latter quotes every string
/// property, so a connection failure reads <c>database '"qln"'</c> (measured; see the rule).</para>
/// <para>Properties that the message template did not consume are appended as <c>k=v</c> pairs,
/// so a structured event loses nothing on the way to a flat line.</para>
/// </remarks>
public sealed class CoaiTextFormatter(string appName, int pid) : ITextFormatter
{
    private static readonly MessageTemplateTextFormatter Message = new("{Message:lj}");

    private static readonly string[] Consumed = ["SourceContext"];

    public void Format(LogEvent logEvent, TextWriter output)
    {
        output.Write('[');
        output.Write(logEvent.Timestamp.UtcDateTime.ToString("HH:mm:ss"));
        output.Write(' ');
        output.Write(LevelToken(logEvent.Level));
        output.Write("] ");
        output.Write(appName);
        output.Write('#');
        output.Write(pid);
        output.Write(' ');
        if (logEvent.Properties.TryGetValue("SourceContext", out var context) &&
            context is ScalarValue { Value: string name })
        {
            output.Write(name);
            output.Write(": ");
        }

        Message.Format(logEvent, output);
        WriteExtras(logEvent, output);
        output.WriteLine();

        if (logEvent.Exception is { } exception)
        {
            output.WriteLine(exception.ToString());
        }
    }

    internal static string LevelToken(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        _ => "FTL",
    };

    /// <summary>Properties the template did not render, as <c> k=v</c> pairs after the message.</summary>
    private static void WriteExtras(LogEvent logEvent, TextWriter output)
    {
        foreach (var (key, value) in logEvent.Properties)
        {
            if (Consumed.Contains(key) || TemplateNames(logEvent).Contains(key))
            {
                continue;
            }

            output.Write(' ');
            output.Write(key);
            output.Write('=');

            // `l` means "no quotes" and it is a STRING specifier: Serilog passes it straight to
            // IFormattable, so a numeric or date property throws FormatException from inside the
            // formatter. The damage was silent and split in two — the file sink had already
            // committed " Round=" and never wrote the newline, so eighteen entries landed on six
            // physical lines; the console sink formats into a buffer first, so it dropped those
            // events ENTIRELY. Found when an audit trail attached a round NUMBER, which is the
            // most ordinary thing a caller could do.
            value.Render(output, value is ScalarValue { Value: string } ? "l" : null);
        }
    }

    private static IEnumerable<string> TemplateNames(LogEvent logEvent) =>
        logEvent.MessageTemplate.Tokens
            .OfType<Serilog.Parsing.PropertyToken>()
            .Select(t => t.PropertyName);
}
