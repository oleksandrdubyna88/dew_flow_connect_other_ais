using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CoaiMcp;

/// <summary>
/// <c>coai-mcp</c> — the MCP half of ConnectOtherAIs.
/// </summary>
/// <remarks>
/// <para>An MCP client (Claude Code, and others) starts this as its own child process and speaks
/// JSON-RPC to it over stdio. <b>stdout carries the protocol</b>: one stray line on it corrupts
/// the stream, and the failure looks like a protocol bug rather than a logging one — so every
/// diagnostic goes to stderr, and nothing here ever writes to stdout except the transport and
/// <c>--help</c>. That is also why the server is built by hand rather than on the SDK's generic
/// host, whose default logging goes to stdout (measured in creds, 2026-08-27).</para>
/// </remarks>
internal static class Program
{
    private const string AppName = "coai-mcp";

    /// <summary>The server's own identity; the CLIENT config key is `coai`, and that shorter
    /// name is what prefixes the tools (`mcp__coai__review_plan`).</summary>
    private const string ServerName = "connect-other-ais";

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

    private static async Task<int> Main(string[] args)
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
                return await ServeAsync();
        }
    }

    private static async Task<int> ServeAsync()
    {
        // stdio host → the console sink goes to stderr (logging-serilog.md, stdio hosts).
        using var log = ServiceDefaults.CoaiLogging.CreateDewFlowLogger(AppName, consoleToStdErr: true);
        try
        {
            var settings = PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
            var launcher = new ProcessLauncher();
            var keys = await new KeyVault(launcher).ReadAsync(Environment.GetEnvironmentVariable(KeyVault.KeyVariable));
            var vaultReadUtc = keys.Available ? DateTime.UtcNow : default;
            log.Information("starting: {Providers} enabled, vault: {Vault}",
                string.Join(",", settings.Providers.Where(p => p.Enabled).Select(p => p.Provider)),
                keys.Available ? "keys loaded" : keys.Unavailability);

            var service = new PanelService(settings, keys, vaultReadUtc, launcher, log);
            var options = new McpServerOptions
            {
                ServerInfo = new Implementation
                {
                    Name = ServerName,
                    Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                },
                ServerInstructions = Instructions,
            };
            options.ToolCollection ??= [];
            foreach (var tool in Tools.All(service))
            {
                options.ToolCollection.Add(tool);
            }

            await using var transport = new StdioServerTransport(ServerName);
            await using var server = McpServer.Create(transport, options);
            await server.RunAsync();
            return 0;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The client went away mid-stream. Not a failure of ours.
            Note("the MCP client closed the connection.");
            return 0;
        }
    }

    internal const string Instructions = """
        ConnectOtherAIs: a review gate run by OTHER vendors' models over your plan and your code.
        The protocol, in order: `open` a session for the repo+branch. `review_plan` sends the plan
        to every enabled provider; `resolve` records your accept/reject decision for EVERY finding
        (a rejection needs a reason). Repeat until the verdict is `proceed`, implement, then
        `review_code` (three independent reviewers per provider), `resolve`, and fix — the same
        loop. `review_code` REFUSES until a plan round has reached `proceed`; skipped stages are
        impossible, not discouraged. `providers` says what is configured and what it authenticates
        as; `status` re-orients a resumed conversation; `ask_human` escalates to the person.
        """;

    internal const string HelpText = """
        coai-mcp — the ConnectOtherAIs review-gate MCP server.

        Takes no arguments; an MCP client starts it and speaks JSON-RPC over stdio.
        Configure it in your client as:

          { "mcpServers": { "coai": { "command": "<full path to coai-mcp>" } } }

        Optional environment: COAI_PROVIDERS, COAI_MODEL_<PROVIDER>, COAI_EXE_<PROVIDER>,
        COAI_MAX_ROUNDS, COAI_GATE_THRESHOLD, COAI_ON_EXHAUSTED (continue|human|escalate),
        COAI_MAX_CONCURRENCY, COAI_MAX_PER_PROVIDER, COAI_REVIEWER_TIMEOUT_MINUTES,
        COAI_DATA_DIR, COAI_CREDS_KEY (the CredsForDevs config-entry key holding vendor keys),
        COAI_LOG_LEVEL.
        """;
}
