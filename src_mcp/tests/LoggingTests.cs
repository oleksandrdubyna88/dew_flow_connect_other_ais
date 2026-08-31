using Xunit;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Serilog.Events;
using Serilog.Parsing;

namespace CoaiMcp.Tests;

/// <summary>
/// The logging contract, observed: a file per run under a UTC day folder, colour that survives
/// redirection, the console stream chosen by the host kind.
/// </summary>
public sealed class LoggingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("coai-logs-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Run_WritesOneLogFile_UnderTheUtcDayFolder()
    {
        var now = new DateTime(2026, 8, 31, 10, 0, 1, DateTimeKind.Utc);
        using (var log = CoaiLogging.CreateDewFlowLogger("app", logsRoot: _root, utcNow: now, consoleWriter: TextWriter.Null))
        {
            log.Information("one line");
        }

        var files = Directory.GetFiles(Path.Combine(_root, "2026-08-31"));
        files.Should().ContainSingle().Which.Should().MatchRegex(@"app-10-00-01-\d+\.log$");
        File.ReadAllText(files[0]).Should().Contain("one line").And.NotContain("\x1b[", "escape codes in a file are noise to every reader");
    }

    [Fact]
    public void SecondRun_WritesASecondFile_NeverARollingAppend()
    {
        var day = new DateTime(2026, 8, 31, 8, 0, 0, DateTimeKind.Utc);
        using (var a = CoaiLogging.CreateDewFlowLogger("app", logsRoot: _root, utcNow: day, consoleWriter: TextWriter.Null))
        {
            a.Information("first run");
        }

        using (var b = CoaiLogging.CreateDewFlowLogger("app", logsRoot: _root, utcNow: day.AddSeconds(7), consoleWriter: TextWriter.Null))
        {
            b.Information("second run");
        }

        Directory.GetFiles(Path.Combine(_root, "2026-08-31")).Should().HaveCount(2);
    }

    [Fact]
    public void ConsoleLine_CarriesAnsiEscapes_EvenIntoARedirectedWriter()
    {
        // The injected StringWriter IS the redirection: no terminal anywhere, escapes must survive.
        var console = new StringWriter();
        using (var log = CoaiLogging.CreateDewFlowLogger("app", logsRoot: _root, consoleWriter: console))
        {
            log.Warning("watch this");
        }

        console.ToString().Should().Contain("\x1b[33mWRN\x1b[0m", "only the level is coloured, strongly");
    }

    [Fact]
    public void Line_CarriesUtcTime_AppName_Pid_AndContext()
    {
        var console = new StringWriter();
        var formatter = new CoaiTextFormatter("coai-mcp", 777);
        var sink = new AnsiConsoleSink(formatter, console);

        sink.Emit(new LogEvent(
            new DateTimeOffset(2026, 8, 31, 15, 30, 0, TimeSpan.FromHours(3)), // 12:30:00 UTC
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse("ready on {Port}"),
            [
                new LogEventProperty("Port", new ScalarValue(5310)),
                new LogEventProperty("SourceContext", new ScalarValue("CoaiMcp.Server")),
            ]));

        var line = console.ToString();
        line.Should().StartWith("[12:30:00 ", "the timestamp is UTC, not the event's +03:00 offset");
        line.Should().Contain("coai-mcp#777 CoaiMcp.Server: ready on 5310");
    }

    [Fact]
    public void ExtraProperties_AppendAsPairs_TemplateOnesDoNot()
    {
        var console = new StringWriter();
        var formatter = new CoaiTextFormatter("app", 1);
        new AnsiConsoleSink(formatter, console).Emit(new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse("took {Elapsed}ms"),
            [
                new LogEventProperty("Elapsed", new ScalarValue(41)),
                new LogEventProperty("Provider", new ScalarValue("codex")),
            ]));

        var line = console.ToString();
        line.Should().Contain("took 41ms").And.Contain("Provider=codex").And.NotContain("Elapsed=41");
    }

    [Fact]
    public void StringProperty_RendersUnquoted_InTheMessage()
    {
        // {Message:lj} rather than RenderMessage(): database 'qln', never database '"qln"'.
        var console = new StringWriter();
        var formatter = new CoaiTextFormatter("app", 1);
        new AnsiConsoleSink(formatter, console).Emit(new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            exception: null,
            new MessageTemplateParser().Parse("database {Name} unreachable"),
            [new LogEventProperty("Name", new ScalarValue("qln"))]));

        console.ToString().Should().Contain("database qln unreachable").And.NotContain("\"qln\"");
    }
}
