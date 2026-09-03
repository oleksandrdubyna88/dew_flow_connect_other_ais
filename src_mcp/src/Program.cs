using System.Reflection;
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

        /// <summary>Print the version and leave.</summary>
        /// <remarks>
        /// It exists because the EXTENSION could not tell what it had installed. The panel used to
        /// remember the number it downloaded, in `globalState` — which VS Code shares between a
        /// local window and a remote one while the binary itself is per side. Measured 2026-09-03:
        /// a WSL side running 0.12.1 was told by its own panel that 0.12.2 was installed and that
        /// there was nothing to update. A binary that can state its own version ends that class of
        /// question: the panel asks the file it is about to describe.
        /// </remarks>
        Version,

        /// <summary>An argument this binary does not take.</summary>
        Usage,

        /// <summary>Speak the protocol.</summary>
        Serve,

        /// <summary>
        /// Ask one local model one question and print the answer — the reviewer "CLI" for an engine
        /// that has none.
        /// </summary>
        /// <remarks>
        /// A mode rather than a second binary: the release publishes one file per platform, and a
        /// reviewer that needed a companion executable beside it would be a reviewer that breaks the
        /// moment somebody copies the one they were told to install.
        /// </remarks>
        AskLocal,
    }

    /// <summary>Which of the three this invocation is. Pure, so it is a unit test.</summary>
    internal static Startup Classify(string[] args) =>
        args.Length == 0
            ? Startup.Serve
            : args[0] is "--help" or "-h" or "help" ? Startup.Help
            : args[0] is "--version" or "-v" or "version" ? Startup.Version
            : args[0] == "--ask-local" ? Startup.AskLocal
            : Startup.Usage;

    private static async Task<int> Main(string[] args)
    {
        switch (Classify(args))
        {
            case Startup.Help:
                // `--help` on stdout: a person running this by hand is not speaking the protocol.
                Console.Out.WriteLine(HelpText);
                return 0;

            case Startup.Version:
                // Same sanction as `--help`, and for the same reason: this mode never speaks the
                // protocol, so stdout is a person's terminal. One line, parseable by a machine.
                Console.Out.WriteLine($"{AppName} {VersionText}");
                return 0;

            case Startup.Usage:
                Note($"unknown argument '{args[0]}' — this binary takes none; an MCP client speaks to it over stdin.");
                return 64; // EX_USAGE

            case Startup.AskLocal:
                return await AskLocalAsync(args);

            default:
                return await ServeAsync();
        }
    }

    /// <summary>
    /// One completion against a local OpenAI-compatible endpoint, written where the executor looks.
    /// </summary>
    /// <remarks>
    /// <para><b>Everything goes to a FILE, in both directions.</b> The prompt arrives as a path and
    /// the answer leaves as one. A review prompt is thousands of characters of diff and schema
    /// carrying quotes, newlines and backticks, and every shell-quoting failure this project has had
    /// came from text on a command line.</para>
    /// <para><b>The tokens are printed on stdout as JSON</b>, which is safe here and only here: this
    /// mode does not speak the protocol, so stdout is free. `LocalRuntime.ReadUsage` reads exactly
    /// that shape.</para>
    /// <para>Exit 0 with no answer file is not possible: a failure exits non-zero AND says why on
    /// stderr, so the round reports the reason rather than "the vendor returned an empty answer".</para>
    /// </remarks>
    internal static async Task<int> AskLocalAsync(string[] args)
    {
        var flags = Flags(args);
        var endpoint = Runners.Reviewers.LocalRuntime.OpenAiBaseOf(
            flags.GetValueOrDefault("--endpoint", Runners.Reviewers.LocalRuntime.DefaultEndpoint));
        var model = flags.GetValueOrDefault("--model", string.Empty);
        var promptFile = flags.GetValueOrDefault("--prompt-file", string.Empty);
        var schemaFile = flags.GetValueOrDefault("--schema-file", string.Empty);
        var outFile = flags.GetValueOrDefault("--out", string.Empty);
        var reasoningEffort = flags.GetValueOrDefault("--reasoning-effort", string.Empty);

        if (promptFile.Length == 0 || outFile.Length == 0)
        {
            Note("--ask-local needs --prompt-file and --out");
            return 64;
        }

        // Hoisted out of the try because the CATCH has to name them: how long this waited and what
        // it was waiting for is the whole difference between "your engine is down" and "your engine
        // is slower than the deadline you gave it".
        var deadline = int.TryParse(flags.GetValueOrDefault("--timeout-seconds", ""), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(10);
        var waited = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var prompt = await File.ReadAllTextAsync(promptFile);
            // A missing schema is refused, not substituted. `{}` was the fallback here — the same
            // unconstrained request that `LocalAsk.RequestBody` was corrected to refuse, left
            // behind in the caller when it was removed from the callee. One decision in two places
            // is how it survived; GPT-5.6-Luna found it on 2026-09-02, and it was the finding this
            // record's author wrongly judged already-fixed from memory.
            if (schemaFile.Length == 0 || !File.Exists(schemaFile))
            {
                Note($"--ask-local needs a finding schema and none was at '{schemaFile}', so no "
                     + "request was sent: an unconstrained request is answered with an invented "
                     + "shape after a full generation has been paid for.");

                return 65; // EX_DATAERR
            }

            var schema = await File.ReadAllTextAsync(schemaFile);
            // Seeded from the prompt BYTES, not from the clock and not from a per-process hash: the
            // same round asked twice is the same request, which is what makes a local reviewer
            // reproducible at all.
            var seed = Runners.Reviewers.LocalAsk.SeedFor(prompt);
            string body;
            try
            {
                body = Runners.Reviewers.LocalAsk.RequestBody(model, prompt, schema, seed, reasoningEffort);
            }
            catch (System.Text.Json.JsonException ex)
            {
                // Before the card is spent. An unconstrained request is answered with an invented
                // shape by every local model tried, so sending one would buy a full generation and
                // an unparseable round — the gate's own reviewers caught that contradiction in an
                // earlier version of this code.
                Note($"the finding schema at {schemaFile} does not parse, so no request was sent: {ex.Message}");

                return 65; // EX_DATAERR
            }

            // The deadline came from the runtime, which derived it from the reviewer timeout the
            // executor enforces. A fixed thirty minutes here was longer than any round, so the only
            // real deadline was being killed — raised by the gate and accepted.
            using var http = new HttpClient { Timeout = deadline };
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"{endpoint.TrimEnd('/')}/chat/completions", content);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Note($"the local engine answered {(int)response.StatusCode}: {Trim(text)}");
                return 70;
            }

            var (answer, usage) = Runners.Reviewers.LocalAsk.ReadResponse(text);
            if (answer is null)
            {
                Note($"the local engine returned no message content: {Trim(text)}");
                return 70;
            }

            await File.WriteAllTextAsync(outFile, answer);
            Console.Out.WriteLine($"{{\"tokensIn\":{usage.TokensIn},\"tokensOut\":{usage.TokensOut}}}");

            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Named rather than swallowed: "connection refused at 11434" is a sentence somebody can
            // act on, and an unreachable engine is an ordinary state of a machine.
            // A TaskCanceledException here is this shim's own deadline, and saying so is the
            // whole reason it has one: "no answer in 290s" is a sentence about the model being slow,
            // while being killed by the executor says nothing at all.
            Note(ex is TaskCanceledException
                ? Runners.Reviewers.LocalAsk.TooSlowMessage(endpoint, waited.Elapsed, deadline)
                : Runners.Reviewers.LocalAsk.UnreachableMessage(endpoint, ex.Message));

            return 69; // EX_UNAVAILABLE
        }
    }

    private static string Trim(string text) =>
        text.Length <= 300 ? text.Replace('\n', ' ') : text[..300].Replace('\n', ' ') + "…";

    /// <summary>`--flag value` pairs. An odd trailing flag simply has no value.</summary>
    private static Dictionary<string, string> Flags(string[] args)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length - 1; i += 1)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)
                && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                flags[args[i]] = args[i + 1];
            }
        }

        return flags;
    }

    private static async Task<int> ServeAsync()
    {
        // stdio host → the console sink goes to stderr (logging-serilog.md, stdio hosts).
        using var log = ServiceDefaults.CoaiLogging.CreateDewFlowLogger(AppName, consoleToStdErr: true);
        try
        {
            // The file the extension writes is the base; the client config env overrides it — a
            // variable in the client is more specific than a file any window may rewrite.
            var configuration = SettingsFile.Layer(
                SettingsFile.DataDirFrom(Environment.GetEnvironmentVariable),
                Environment.GetEnvironmentVariable);
            var settings = PanelSettings.FromEnvironment(configuration);
            // The tracker is what lets a LATER server collect reviewers this one leaves behind if
            // it dies: the timeout kill is performed by the parent, so it cannot run when the
            // parent is what went away.
            var tracking = new Runners.Processes.ProcessTracking(
                settings.DataDir,
                message => log.Warning("process tracking: {Detail}", message));
            var launcher = new ProcessLauncher(tracking);
            var keys = await new KeyVault(launcher).ReadAsync(Environment.GetEnvironmentVariable(KeyVault.KeyVariable));
            var vaultReadUtc = keys.Available ? DateTime.UtcNow : default;
            log.Information("starting: {Providers} enabled, vault: {Vault}",
                string.Join(",", settings.Providers.Where(p => p.Enabled).Select(p => p.Provider)),
                keys.Available ? "keys loaded" : keys.Unavailability);

            // A setting this build cannot understand is said out loud at startup, because the
            // alternative is what actually happened: a configuration that had been applied, read and
            // reloaded correctly looked broken for twenty minutes, and the one thing that would have
            // ended it in a second was this line.
            foreach (var mismatch in settings.Unrecognised)
            {
                log.Warning("{Mismatch}", mismatch);
            }

            // The file the panel writes is re-read per call, so a vendor or a threshold changed
            // in the sidebar reaches the NEXT round without restarting the MCP client.
            var host = new PanelServiceHost(Environment.GetEnvironmentVariable, keys, vaultReadUtc, launcher, log);
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
            foreach (var tool in Tools.All(host))
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
        It is ADDITIONAL: run whatever review your own workflow performs exactly as you would
        have, and start it in PARALLEL with these tools rather than instead of them. A
        different vendor's model answers the questions your own model is worst placed to
        answer; your own reviewers read the whole change in context. Neither replaces the
        other, and a `call_human` verdict stops the SHIPPING, not the task.
        The protocol, in order: `open` a session for the repo+branch. `review_plan` sends the plan
        to every enabled provider; `resolve` records your accept/reject decision for EVERY finding
        (a rejection needs a reason). Repeat until the verdict is `proceed`, implement, then
        `review_code` (three independent reviewers per provider), `resolve`, and fix — the same
        loop. `review_code` REFUSES until a plan round has reached `proceed`; skipped stages are
        impossible, not discouraged. `providers` says what is configured and what it authenticates
        as; `status` re-orients a resumed conversation; `ask_human` escalates to the person.
        """;

    /// <summary>
    /// The version stamped into this binary, or <c>0.0.0</c> when nothing stamped it.
    /// </summary>
    /// <remarks>
    /// <para><b>Never a default 1.0.0.</b> An unstamped build must read as OLDER than every
    /// published release, because the panel compares this number against the newest tag: a 1.0.0
    /// would have suppressed the update button for ever. <c>Version</c> is therefore pinned to
    /// <c>0.0.0</c> in the csproj and the release passes the tag's version over it.</para>
    /// <para>The release smoke step asserts this equals the tag, so a stamping step that stops
    /// working fails the release rather than shipping a binary that misreports itself.</para>
    /// </remarks>
    internal static string VersionText =>
        VersionFrom(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// The version out of an informational version string — pure, so it is a table in the tests.
    /// </summary>
    /// <remarks>
    /// Everything from the FIRST <c>+</c> is dropped, not a <c>+sha</c> suffix specifically: source
    /// link stamps a commit there, and a build server is free to stamp something else. Whatever it
    /// is, it is build metadata and not a version anyone can compare.
    /// </remarks>
    internal static string VersionFrom(string? informational)
    {
        var text = (informational ?? string.Empty).Trim();
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        var version = (plus < 0 ? text : text[..plus]).Trim();

        return version.Length == 0 ? "0.0.0" : version;
    }

    internal const string HelpText = """
        coai-mcp — the ConnectOtherAIs review-gate MCP server.

        Takes no arguments; an MCP client starts it and speaks JSON-RPC over stdio.
        `--version` prints the version this binary was stamped with, and nothing else.
        Configure it in your client as:

          { "mcpServers": { "coai": { "command": "<full path to coai-mcp>" } } }

        Optional environment: COAI_PROVIDERS, COAI_MODEL_<PROVIDER>, COAI_EXE_<PROVIDER>,
        COAI_MAX_ROUNDS, COAI_GATE_THRESHOLD, COAI_ON_EXHAUSTED (continue|human|escalate),
        COAI_MAX_CONCURRENCY, COAI_MAX_PER_PROVIDER, COAI_LOCAL_CONCURRENCY,
        COAI_REVIEWER_TIMEOUT_MINUTES,
        COAI_DATA_DIR, COAI_RATE_LIMIT_BACKOFF_SECONDS, COAI_ESCALATION_MINUTES
        (or COAI_ESCALATION_SECONDS, which wins),
        COAI_CREDS_KEY (the CredsForDevs config-entry key holding vendor keys),
        COAI_LOG_LEVEL.
        """;
}
