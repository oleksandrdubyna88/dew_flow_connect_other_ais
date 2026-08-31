using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace CoaiMcp.ServiceDefaults;

/// <summary>
/// The one place logging is configured in this repository (logging-serilog.md: never in a library,
/// never in more than one place). Two destinations, always: the console in colour, and a file per
/// run under <c>logs/{yyyy-MM-dd}/</c>.
/// </summary>
/// <remarks>
/// <para><b>Deviation from the rule's C# shape, stated rather than hidden:</b> the rule's
/// <c>AddDewFlowLogging(this IHostApplicationBuilder …)</c> assumes the generic host, and this
/// repository's only host is a hand-built AOT stdio process that deliberately takes no host
/// builder (the SDK's hosted default logs to stdout — the measured reason creds-mcp builds its
/// server by hand). So the CONTRACT is honoured — two sinks, the path shape, UTC, stderr for
/// stdio, levels from configuration — through a factory instead of an extension, the same way the
/// rule's Rust section shares the contract without the library.</para>
/// <para>Level floor comes from <c>COAI_LOG_LEVEL</c> (default Information) until the host gains a
/// config file in epic 04 — a config edit and a restart, never an edited call site.</para>
/// </remarks>
public static class CoaiLogging
{
    /// <param name="appName">Enriched onto every line; also the file-name prefix.</param>
    /// <param name="consoleToStdErr">stdio hosts MUST pass true: stdout carries the protocol.</param>
    /// <param name="logsRoot">Injectable for tests; defaults to <c>logs/</c> beside the binary.</param>
    /// <param name="utcNow">Injectable for tests; the run's file name is taken ONCE, here.</param>
    /// <param name="consoleWriter">Injectable for tests; defaults to the chosen console stream.</param>
    public static Logger CreateDewFlowLogger(
        string appName,
        bool consoleToStdErr = false,
        string? logsRoot = null,
        DateTime? utcNow = null,
        TextWriter? consoleWriter = null)
    {
        var pid = Environment.ProcessId;
        var formatter = new CoaiTextFormatter(appName, pid);
        var file = CoaiLogPath.For(
            logsRoot ?? Path.Combine(AppContext.BaseDirectory, "logs"),
            appName,
            utcNow ?? DateTime.UtcNow,
            pid);
        var console = consoleWriter ?? (consoleToStdErr ? Console.Error : Console.Out);

        return new LoggerConfiguration()
            .MinimumLevel.Is(FloorFromEnvironment())
            .WriteTo.Sink(new AnsiConsoleSink(formatter, console))
            .WriteTo.File(formatter, file, shared: false)
            .CreateLogger();
    }

    internal static LogEventLevel FloorFromEnvironment() =>
        Environment.GetEnvironmentVariable("COAI_LOG_LEVEL")?.ToLowerInvariant() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            _ => LogEventLevel.Information,
        };
}
