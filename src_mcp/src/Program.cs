namespace CoaiMcp;

/// <summary>
/// <c>coai-mcp</c> — the MCP half of ConnectOtherAIs.
/// </summary>
/// <remarks>
/// <para>An MCP client (Claude Code, and others) starts this as its own child process and speaks
/// JSON-RPC to it over stdio. <b>stdout carries the protocol</b>: one stray line on it corrupts
/// the stream, and the failure looks like a protocol bug rather than a logging one — so every
/// diagnostic goes to stderr, and nothing here ever writes to stdout except the transport and
/// <c>--help</c>.</para>
/// <para>This is the epic-01 skeleton: it classifies its invocation, wires the logging contract,
/// and refuses to serve until epic 04 gives it a server. The refusal is deliberate — a binary
/// that pretends to serve is worse than one that says it cannot yet.</para>
/// </remarks>
internal static class Program
{
    private const string AppName = "coai-mcp";

    private static void Note(string message) => Console.Error.WriteLine($"[{AppName}] {message}");

    /// <summary>What this process was started to do, before any of it happens.</summary>
    internal enum Startup
    {
        /// <summary>Print the help and leave.</summary>
        Help,

        /// <summary>An argument this binary does not take.</summary>
        Usage,

        /// <summary>Speak the protocol.</summary>
        Serve,
    }

    /// <summary>Which of the three this invocation is. Pure, so it is a unit test.</summary>
    internal static Startup Classify(string[] args) =>
        args.Length == 0
            ? Startup.Serve
            : args[0] is "--help" or "-h" or "help" ? Startup.Help : Startup.Usage;

    private static int Main(string[] args)
    {
        switch (Classify(args))
        {
            case Startup.Help:
                // `--help` on stdout: a person running this by hand is not speaking the protocol.
                Console.Out.WriteLine(HelpText);
                return 0;

            case Startup.Usage:
                Note($"unknown argument '{args[0]}' — this binary takes none; an MCP client speaks to it over stdin.");
                return 64; // EX_USAGE

            default:
                return Serve();
        }
    }

    private static int Serve()
    {
        // stdio host → the console sink goes to stderr (logging-serilog.md, stdio hosts).
        using var log = ServiceDefaults.CoaiLogging.CreateDewFlowLogger(AppName, consoleToStdErr: true);
        log.Information("coai-mcp starting — skeleton build, no server yet");
        Note("this build carries no MCP server yet (epic 04). It exists to prove the pipeline.");
        return 2;
    }

    internal const string HelpText = """
        coai-mcp — the ConnectOtherAIs review-gate MCP server.

        Takes no arguments; an MCP client starts it and speaks JSON-RPC over stdio.
        Configure it in your client as:

          { "mcpServers": { "coai": { "command": "<full path to coai-mcp>" } } }

        This is a skeleton build: the tool surface arrives with epic 04.
        """;
}
