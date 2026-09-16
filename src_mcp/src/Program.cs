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
    /// <summary>
    /// What this program calls itself — defined where the code that READS its stderr can see it.
    /// </summary>
    /// <remarks>
    /// It was a private constant here, so the tag in front of every note this process writes was
    /// unknowable to <c>BoundedScheduler</c>, which reads that stream to explain a failure. Two
    /// things needed it on 2026-09-08: anchoring a progress recogniser, and knowing which lines are
    /// OURS and therefore worth quoting whole. See <see cref="Runners.Reviewers.ShimNotes"/>.
    /// </remarks>
    private const string AppName = Runners.Reviewers.ShimNotes.AppName;

    /// <summary>The server's own identity; the CLIENT config key is `coai`, and that shorter
    /// name is what prefixes the tools (`mcp__coai__review_plan`).</summary>
    private const string ServerName = "connect-other-ais";

    private static void Note(string message) =>
        Console.Error.WriteLine($"{Runners.Reviewers.ShimNotes.Prefix}{message}");

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

        /// <summary>Sends the pairs a person kept. The only mode that leaves this machine.</summary>
        UploadPairs,

        /// <summary>The collected pairs, for the review page to render.</summary>
        Pairs,

        /// <summary>A batch of keep/drop decisions, file-in as `--findings-many` takes its keys.</summary>
        PairsKeep,

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

        /// <summary>One review, sent to a Team server instead of a CLI on this machine.</summary>
        AskRemote,

        /// <summary>
        /// Print the rounds database as JSON and leave.
        /// </summary>
        /// <remarks>
        /// For the panel, which owns no SQLite of its own and should not have to: the alternative
        /// was a WebAssembly build in the VSIX or a native module per platform, to ask questions of
        /// a file this binary already writes and whose schema it owns. A read-only mode costs
        /// nothing and keeps every query beside its table.
        /// </remarks>
        Log,

        /// <summary>
        /// Print every provider's health as JSON and leave — what the `providers` tool answers.
        /// </summary>
        /// <remarks>
        /// For the PANEL, which badges a reviewer this server cannot run. It reads this the same way
        /// it already reads <c>--log</c>, and the alternative — deciding availability again in
        /// TypeScript — is the second copy of a decision this repository has twice paid for.
        /// </remarks>
        Providers,

        /// <summary>
        /// Print ONE round's findings as JSON and leave — what an opened row of the log asks for.
        /// </summary>
        /// <remarks>
        /// It exists so the LIST does not have to carry them. Measured before it was written:
        /// <c>--log</c> answered 3.83 MB, of which 3.78 MB was findings for rounds nobody had
        /// opened.
        /// </remarks>
        Findings,
        FindingsMany,

        /// <summary>
        /// Print the accepted findings as corpus material, with the funnel that produced them.
        /// </summary>
        /// <remarks>
        /// <para>The same reasoning as <see cref="Log"/> — the panel owns no SQLite — for a different
        /// question. <c>--log</c> answers "what happened in this round"; this answers "which defects
        /// are worth keeping", which filters on things the log has no opinion about.</para>
        /// <para>It MIGRATES before it reads, unlike the other read-only modes, because it is the
        /// first thing that asks for the collector's columns and a person may well run it before the
        /// server has opened the database with a build that has them.</para>
        /// </remarks>
        Bugs,

        /// <summary>
        /// Read methods out of source and rewrite them so nothing of this project is left in them.
        /// </summary>
        /// <remarks>
        /// <para>A MODE rather than the sidecar it was first planned as. The release publishes one
        /// file per platform, and a companion executable beside it is one somebody eventually copies
        /// without — the reasoning that already made <see cref="AskLocal"/> a mode. tree-sitter
        /// reaches its grammars by P/Invoke, which Native AOT carries without complaint, so a second
        /// binary would have bought a second release line and nothing else.</para>
        /// <para>Files in, files out, and batched: a method's source is too big for an argument and
        /// the answers are bigger, and the cost here is the process rather than the parse.</para>
        /// </remarks>
        Normalize,

        /// <summary>Decide what became of every unprocessed candidate, and write it down.</summary>
        /// <remarks>
        /// The half that makes the rest real: a classifier that never persists leaves every
        /// candidate unprocessed in the shipped product however well it classifies.
        /// </remarks>
        Collect,
    }

    /// <summary>Which of the three this invocation is. Pure, so it is a unit test.</summary>
    internal static Startup Classify(string[] args) =>
        args.Length == 0
            ? Startup.Serve
            // A switch rather than a ternary chain: the chain was six deep and every new one-shot
            // mode made it deeper, which an analyser reads as one expression doing seven jobs.
            : args[0] switch
            {
                "--help" or "-h" or "help" => Startup.Help,
                "--version" or "-v" or "version" => Startup.Version,
                "--ask-local" => Startup.AskLocal,
                "--ask-remote" => Startup.AskRemote,
                "--log" => Startup.Log,
                "--findings" => Startup.Findings,
                "--findings-many" => Startup.FindingsMany,
                "--bugs-json" => Startup.Bugs,
                "--normalize" => Startup.Normalize,
                "--collect-bugs" => Startup.Collect,
                "--pairs-json" => Startup.Pairs,
                "--pairs-keep" => Startup.PairsKeep,
                "--upload-pairs" => Startup.UploadPairs,
                "--providers" => Startup.Providers,
                _ => Startup.Usage,
            };

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

            case Startup.AskRemote:
                return await AskRemote.RunAsync(Flags(args), Note, Console.Out);

            case Startup.Log:
                return LogJson(args);

            case Startup.Findings:
                return FindingsJson(args);

            case Startup.FindingsMany:
                return FindingsManyJson(args);

            case Startup.Bugs:
                return BugsJson(args);

            case Startup.Normalize:
                return NormalizeJson(args);

            case Startup.Collect:
                return await CollectBugsAsync(args);

            case Startup.Pairs:
                return PairsJson(args);

            case Startup.PairsKeep:
                return PairsKeep(args);

            case Startup.UploadPairs:
                return await UploadPairsAsync(args);

            case Startup.Providers:
                return await ProvidersJsonAsync();

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
    /// <summary>
    /// The rounds database on stdout, as JSON. Never the protocol, so stdout is a person's terminal.
    /// </summary>
    /// <remarks>
    /// An empty log — no database yet, or one that cannot be read — is an empty result and exit 0,
    /// not an error: a panel asking a machine that has never run a round is asking a fair question
    /// and deserves an answer it can render.
    /// </remarks>
    /// <summary>
    /// Every configured provider's health, as the `providers` tool answers it, on stdout.
    /// </summary>
    /// <remarks>
    /// <para>A one-shot CLI mode: selected from <c>args[0]</c> before any transport is opened, so it
    /// never speaks JSON-RPC and its stdout is its entire interface. The stdout non-negotiable in
    /// CLAUDE.md is about the PROTOCOL path, and names this flag among the modes that legitimately
    /// write there.</para>
    /// <para>It exists so the panel can BADGE a reviewer the server cannot run without deciding
    /// availability for itself. That decision has one author — <c>RuntimeResolution.AuthOf</c> — and
    /// this repository has twice paid for a second copy of it. The panel already reads
    /// <c>--log</c> exactly this way.</para>
    /// </remarks>
    private static async Task<int> ProvidersJsonAsync()
    {
        try
        {
            return await ProvidersJsonCoreAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or System.Text.Json.JsonException or InvalidOperationException)
        {
            // A settings file that will not open, a vault read that threw. The panel reads a
            // non-zero exit as "asked and could not answer", which is right — but a person at a
            // terminal deserves a sentence rather than a stack trace. Raised on epic 3's code round.
            Note($"could not read this machine's provider configuration: {e.Message}");

            return 74; // EX_IOERR
        }
    }

    private static async Task<int> ProvidersJsonCoreAsync()
    {
        // Layered exactly as `ServeAsync` layers it. Reading the ENVIRONMENT alone was the first
        // version, and the seam check caught it in one run: the panel's own settings file was
        // ignored, so this mode answered about the DEFAULT vendors and would have badged a
        // configuration nobody has. The variable still outranks the file, key by key, as everywhere.
        var configuration = Server.SettingsFile.Layer(
            Server.SettingsFile.DataDirFrom(Environment.GetEnvironmentVariable),
            Environment.GetEnvironmentVariable);
        var settings = Server.PanelSettings.FromEnvironment(configuration);
        var launcher = new Runners.Processes.ProcessLauncher();
        // The same read `ServeAsync` does, so a vendor whose key is in the vault is reported as
        // runnable here too — otherwise this mode would badge half a configuration as unavailable.
        var keys = await new Server.KeyVault(launcher)
            .ReadAsync(Environment.GetEnvironmentVariable(Server.KeyVault.KeyVariable));
        var service = new Server.PanelService(
            settings, keys, DateTime.UtcNow, launcher, Serilog.Core.Logger.None);

        await Console.Out.WriteLineAsync(await service.ProvidersAsync());

        return 0;
    }

    private static int LogJson(string[] args)
    {
        var settings = Server.PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        try
        {
            // A caller that cannot page also cannot ask for a second page, so its default stays
            // the three hundred it has always been. (CodeRabbit, on the pull request.)
            var paged = Paged(args);
            Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                Store.RoundsQuery.Read(
                    settings.DataDir,
                    Limit(args, paged ? Store.RoundsQuery.DefaultLimit : Store.RoundsQuery.LegacyLimit),
                    Before(args),
                    withFindings: !paged),
                Server.ServerJsonContext.Default.LoggedLog));
        }
        catch (Exception e) when (Unreadable(e))
        {
            Note(WhyUnreadable(e));
            Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                new Store.LoggedLog([], [], [], new Store.LoggedTotals()), Server.ServerJsonContext.Default.LoggedLog));

            // A PAGED caller is told, because it can act on it — 74 is the database, not the log.
            // The legacy shape keeps exit 0 and an empty log, which is what it has always answered
            // and what an extension too old to read a code expects. (CodeRabbit, on the PR.)
            return Paged(args) ? 74 : 0; // EX_IOERR
        }

        return 0;
    }

    /// <summary>
    /// The accepted findings as corpus material, with the funnel that produced them.
    /// </summary>
    /// <remarks>
    /// <para><b>It opens the database for WRITING first, and only to migrate it.</b> Every other
    /// one-shot read here is read-only, and this one would be too but for being the first caller
    /// that wants the collector's columns: a person running it before the server has opened the file
    /// with a build that has them would otherwise be told, truthfully and uselessly, that there are
    /// nought candidates. <c>RoundsDb.Open</c> is the only thing that migrates and it is idempotent
    /// through <c>user_version</c>, so this costs an open and changes nothing on a current file.</para>
    /// <para>The limit goes through the shared <see cref="Limit"/> helper, which clamps to
    /// <c>RoundsQuery.MaxLimit</c> — the same 1 000 <c>BugsQuery</c> clamps to, deliberately, so a
    /// caller cannot discover that two maxima disagree.</para>
    /// <para>74 (EX_IOERR) for a database that would not be read, as <c>--log --paged</c> answers:
    /// an empty corpus and a success would read as "no material", which is the one wrong answer.</para>
    /// </remarks>
    /// <summary>
    /// Read methods out of source and rewrite them, a batch at a time.
    /// </summary>
    /// <remarks>
    /// <para>Files in, files out: a method's source will not fit in an argument on Windows and the
    /// answers are larger than the asks. <c>--ask-local</c> already takes its prompt from a file for
    /// the same reason, and this follows it rather than inventing a second convention.</para>
    /// <para>Exit codes say which thing went wrong, because they ask for different actions. <b>66</b>
    /// (EX_NOINPUT) is a request file that is missing or will not parse — the caller wrote it, so the
    /// caller can fix it. <b>73</b> (EX_CANTCREAT) is an answer file that could not be written, which
    /// is a disk rather than a request. Nothing here exits non-zero because a method could not be
    /// located: that is an ANSWER, carried per item as a skip, and a batch of fifty where two failed
    /// to resolve is a successful batch.</para>
    /// </remarks>
    /// <summary>One collector pass over the unprocessed candidates.</summary>
    /// <remarks>
    /// Opens the database for WRITING, which is the point: this is the one mode that DECIDES things
    /// rather than reporting them. 74 (EX_IOERR) when it cannot be opened, because an empty summary
    /// and a success would read as "there was nothing to collect".
    /// </remarks>
    internal static async Task<int> CollectBugsAsync(string[] args)
    {
        var settings = Server.PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        using var db = Store.RoundsDb.Open(settings.DataDir, Serilog.Core.Logger.None);
        if (db is null)
        {
            Note("the rounds database could not be opened; no candidate can be collected from it");
            return 74; // EX_IOERR
        }

        var run = new Collecting.CollectRun(
            new Runners.Collecting.Collector(
                new Runners.Collecting.GitHistory(new ProcessLauncher()),
                new Normalizer.TreeSitterNormalizer()),
            TimeProvider.System,
            // Progress on stderr, because stdout is the JSON interface — a run of two hundred
            // candidates is minutes of git subprocesses and a silent terminal cannot be told from a
            // hung one.
            Console.Error);

        // Before anything is started: a run whose window closed stopped saying it was alive, and
        // until it is ended the panel reads it as in flight for ever. Only a STALE one is taken —
        // the heartbeat is what separates an abandoned run from one happening in another process
        // right now, which a check on `finished_utc` alone would have killed. (Plan round, gemini.)
        var ended = db.SweepStaleCollectRuns(StaleAfter);
        if (ended > 0)
        {
            Note($"ended {ended} run(s) that stopped reporting; their claimed findings keep their outcomes");
        }

        var flags = Flags(args);
        flags.TryGetValue("--model", out var model);

        var summary = await run.RunAsync(
            settings.DataDir,
            db,
            Limit(args, Store.BugsQuery.DefaultLimit),
            all: Array.IndexOf(args, "--all") >= 0,
            model: model ?? string.Empty);

        if (summary.Refusal.Length > 0)
        {
            // A named model that may not be shown un-anonymised finding text.
            //
            // 65 and emphatically NOT 64. This binary KNOWS `--collect-bugs`; it is refusing an
            // ARGUMENT, and 64 is reserved for a mode the binary does not have, so a caller can tell
            // an old server from a bad request and fall back. A request fault wearing 64 would send
            // the caller down that fallback and hide itself behind a successful-looking answer —
            // which is why `--findings-many` answers an unreadable keys file with 65 too.
            // (PROJECT.md, the one-shot mode paragraph; raised at the story-5 plan round.)
            Note(summary.Refusal);
            return 65; // EX_DATAERR
        }

        Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            summary, Server.ServerJsonContext.Default.CollectSummary));

        return 0;
    }

    /// <summary>How long a run may go without saying it is alive before it is presumed gone.</summary>
    /// <remarks>
    /// Thirty minutes, and the number is `chatStoreSweep`'s rather than a new one, for its reason: it
    /// is thirty beats wide, so a sleeping laptop, a host paused under a debugger and a window that
    /// has just reloaded are all still believed. The cost is that a killed run reads as in flight for
    /// half an hour, which nobody can see.
    /// </remarks>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>The collected pairs, with whatever a person has decided about them.</summary>
    /// <remarks>
    /// Opens for writing because it MIGRATES first, exactly as `--bugs-json` does and for the same
    /// reason: a person may run it before the server has ever opened the file with a build that has
    /// the pairs table. 74 when the file will not open, because an empty list and a success would
    /// read as "there is nothing to review".
    /// </remarks>
    internal static int PairsJson(string[] args)
    {
        var settings = Server.PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        using var db = Store.RoundsDb.Open(settings.DataDir, Serilog.Core.Logger.None);
        if (db is null)
        {
            Note("the rounds database could not be opened; no pair can be read from it");
            return 74; // EX_IOERR
        }

        // A database that OPENED can still fail while it is read — a locked file, a disk that went
        // away, a row that will not decode. `Open` answers null only for the open itself, so without
        // this the exception escapes as an unhandled process failure rather than the 74 every other
        // database mode here answers. (CodeRabbit.)
        try
        {
            Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                new Collecting.PairsAnswer([.. db.Pairs(Limit(args, Store.BugsQuery.DefaultLimit))]),
                Server.ServerJsonContext.Default.PairsAnswer));
        }
        catch (Exception e) when (Unreadable(e))
        {
            Note(WhyUnreadable(e));
            return 74; // EX_IOERR
        }

        return 0;
    }

    /// <summary>Writes a batch of keep/drop decisions.</summary>
    /// <remarks>
    /// <para><b>A file in, not one spawn per decision</b>, on `--findings-many`'s precedent: a review
    /// of two hundred pairs is two hundred process launches otherwise.</para>
    /// <para><b>65 and never 64 for a bad file.</b> 64 is how a caller detects a binary too old for a
    /// mode and falls back; a request fault wearing that code would send it down the fallback and
    /// hide itself behind a successful-looking answer. This binary KNOWS the mode.</para>
    /// </remarks>
    internal static int PairsKeep(string[] args)
    {
        var asked = Decisions(args, out var refusal);
        if (refusal.Length > 0)
        {
            Note(refusal);
            return 65; // EX_DATAERR
        }

        var settings = Server.PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        using var db = Store.RoundsDb.Open(settings.DataDir, Serilog.Core.Logger.None);
        if (db is null)
        {
            Note("the rounds database could not be opened; no decision can be written to it");
            return 74; // EX_IOERR
        }

        try
        {
            var decided = db.RecordKeep(
                [.. asked.Select(one => new Core.Collecting.KeepDecision(one.FindingId, one.Keep))]);
            Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                new Collecting.KeepAnswer(decided), Server.ServerJsonContext.Default.KeepAnswer));
        }
        catch (Exception e) when (Unreadable(e))
        {
            Note(WhyUnreadable(e));
            return 74; // EX_IOERR
        }

        return 0;
    }

    /// <summary>The decisions a file asked for, or why it is not a request.</summary>
    /// <remarks>
    /// Split out so the mode above is the DATABASE work and this is the reading of a document —
    /// which is also what keeps each of them inside the complexity the conventions allow. Every
    /// refusal here is a request fault and its caller answers 65; none is ever 64, because 64 means
    /// "this binary does not have that mode" and sends a caller down a fallback.
    /// </remarks>
    private static IReadOnlyList<Collecting.KeepAsk> Decisions(string[] args, out string refusal)
    {
        var flags = Flags(args);
        // Whitespace as well as absent: `--in ""` reaches `File.ReadAllText` as an ArgumentException.
        if (!flags.TryGetValue("--in", out var input) || string.IsNullOrWhiteSpace(input))
        {
            refusal = "--pairs-keep needs --in <decisions.json>";
            return [];
        }

        Collecting.KeepRequest? request;
        try
        {
            request = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(input), Server.ServerJsonContext.Default.KeepRequest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException
                                      or System.Text.Json.JsonException)
        {
            refusal = $"--pairs-keep could not read {input}: {e.Message}";
            return [];
        }

        return Wanted(request, out refusal);
    }

    /// <summary>What a parsed document actually asked for, once it is known to be a request.</summary>
    /// <remarks>
    /// A document with NO `items` is malformed, not an empty batch: `{}` used to become an empty list,
    /// commit nothing and exit 0, so a misspelled field looked successfully processed. An explicitly
    /// empty array stays a legitimate no-op.
    /// </remarks>
    private static IReadOnlyList<Collecting.KeepAsk> Wanted(
        Collecting.KeepRequest? request, out string refusal)
    {
        if (request?.Items is not { } asked)
        {
            refusal = "--pairs-keep: the document has no `items` list";
            return [];
        }

        // Validated because it came from another process: a keep outside the three it may be is a
        // request fault, not a value to write and puzzle over later.
        refusal = Array.Exists([.. asked], one => !Core.Collecting.Keep.IsDecision(one.Keep))
            ? "--pairs-keep: every decision must be -1 (undecided), 0 (dropped) or 1 (kept)"
            : string.Empty;

        return asked;
    }
    /// <summary>Sends the kept pairs to an ingest server.</summary>
    /// <remarks>
    /// <para><b>The key is NEVER an argument.</b> An argument is in process listings and in shell
    /// history, which this family's secret rule forbids outright. It comes from `COAI_BUGS_KEY`, or
    /// from a file named by `--key-file` — a path is not a secret. (Plan round, codex.)</para>
    /// <para><b>And nothing is marked until the server answers.</b> A pair recorded as sent before
    /// the acknowledgement is a pair this client skips for ever.</para>
    /// </remarks>
    internal static async Task<int> UploadPairsAsync(string[] args)
    {
        var flags = Flags(args);
        if (!flags.TryGetValue("--server", out var where) || !Uri.TryCreate(where, UriKind.Absolute, out var server))
        {
            Note("--upload-pairs needs --server <https://host>");
            return 65; // EX_DATAERR
        }

        // Loopback may be plain http for a person trying it out; anything else must be TLS, because
        // a bearer key over plain http is a key an observer keeps. (Plan round, codex.)
        if (server.Scheme != Uri.UriSchemeHttps && !server.IsLoopback)
        {
            Note($"{server} is not https; a key sent over plain http is a key somebody else has");
            return 65; // EX_DATAERR
        }

        var key = Key(flags);
        if (key.Length == 0)
        {
            Note("no key: set COAI_BUGS_KEY, or pass --key-file <path>. Never --key, because an "
                 + "argument is in process listings and shell history.");
            return 65; // EX_DATAERR
        }

        var settings = Server.PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        using var db = Store.RoundsDb.Open(settings.DataDir, Serilog.Core.Logger.None);
        if (db is null)
        {
            Note("the rounds database could not be opened; there is nothing to send from it");
            return 74; // EX_IOERR
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var run = new Collecting.UploadRun(http, Console.Error);
        var summary = await run.RunAsync(
            db, server, key, Limit(args, Collecting.UploadRun.PerBatch));

        Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            summary, Server.ServerJsonContext.Default.UploadSummary));

        // A transport failure is 69: nothing is wrong with the request, and the caller should try
        // again rather than change anything.
        return summary.Trouble.Length > 0 ? 69 : 0; // EX_UNAVAILABLE
    }

    /// <summary>The key, from the environment or a file. Never from argv.</summary>
    private static string Key(IReadOnlyDictionary<string, string> flags)
    {
        if (flags.TryGetValue("--key-file", out var file) && File.Exists(file))
        {
            return File.ReadAllText(file).Trim();
        }

        return (Environment.GetEnvironmentVariable("COAI_BUGS_KEY") ?? string.Empty).Trim();
    }

    internal static int NormalizeJson(string[] args)
    {
        var flags = Flags(args);
        if (!flags.TryGetValue("--in", out var input) || !flags.TryGetValue("--out", out var output))
        {
            Note("--normalize needs --in <requests.json> and --out <answers.json>");
            return 64; // EX_USAGE
        }

        Normalising.NormalizeRequest? request;
        try
        {
            request = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(input), Server.ServerJsonContext.Default.NormalizeRequest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Note($"--normalize could not read the request at '{input}': {e.Message}");
            return 66; // EX_NOINPUT
        }

        var answer = new Normalising.NormalizeMode(new Normalizer.TreeSitterNormalizer())
            .Run(request ?? new Normalising.NormalizeRequest());

        try
        {
            File.WriteAllText(
                output,
                System.Text.Json.JsonSerializer.Serialize(answer, Server.ServerJsonContext.Default.NormalizeResult));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Note($"--normalize could not write the answers to '{output}': {e.Message}");
            return 73; // EX_CANTCREAT
        }

        return 0;
    }

    internal static int BugsJson(string[] args)
    {
        var settings = Server.PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        try
        {
            using (var migrated = Store.RoundsDb.Open(settings.DataDir, Serilog.Core.Logger.None))
            {
                // Opened and closed: the migration is the whole point of the statement. A file that
                // would not open is REPORTED rather than answered with an empty corpus — "no
                // material" and "the database would not open" ask for different things from whoever
                // reads it, and `Open` swallows the exception because a round must survive a
                // projection it cannot write. Nothing here is a round.
                if (migrated is null)
                {
                    Note("the rounds database could not be opened; no corpus can be read from it");
                    return 74; // EX_IOERR
                }

                // And the sweep, here as well as in the collect mode. This is the mode the PANEL
                // calls, and without it an abandoned run reads as `running` for ever: the Collect
                // button stays disabled, and the only thing that would clear it is a collect the
                // button will not start. A deadlock with no way out of it from the UI. Safe to run
                // from a reader because the heartbeat is what decides staleness, so a run happening
                // in another process right now is untouched. (Code round, codex and gemini.)
                migrated.SweepStaleCollectRuns(StaleAfter);
            }

            Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                Store.BugsQuery.Read(
                    settings.DataDir,
                    Limit(args, Store.BugsQuery.DefaultLimit),
                    all: Array.IndexOf(args, "--all") >= 0),
                Server.ServerJsonContext.Default.BugCorpus));
        }
        catch (Exception e) when (Unreadable(e))
        {
            Note(WhyUnreadable(e));
            return 74; // EX_IOERR
        }

        return 0;
    }

    /// <summary>
    /// One round's findings, for a row somebody just opened.
    /// </summary>
    /// <remarks>
    /// <para>Exit code carries the distinction the page cannot otherwise make: <b>0</b> and a list
    /// means the database holds this round and this is what it found — an empty list is then a clean
    /// round. <b>69</b> (EX_UNAVAILABLE) means it has never heard of the round, so its findings were
    /// recorded nowhere and "nothing found" would be a lie about it. <b>74</b> (EX_IOERR) is a
    /// database that could not be READ, which is a third thing again: the page draws it as a failed
    /// read with a retry, because the round may be perfectly fine. The code round caught these last
    /// two being the same number, which would have told somebody a round was never recorded because
    /// a file was momentarily locked.</para>
    /// <para>Without that, an opened row has one blank for four different truths, which is the defect
    /// this mode was added to end.</para>
    /// </remarks>
    /// <summary>
    /// The findings of MANY rounds, in one process.
    /// </summary>
    /// <remarks>
    /// <para><b>Its own <c>args[0]</c>, and that is the whole design.</b> A flag on <c>--findings</c>
    /// would not be refused by an older binary: <c>Classify</c> would accept <c>--findings</c>, find
    /// no <c>--session</c>, and exit <b>69</b> — "no such round … its findings were never recorded" —
    /// which the extension faithfully renders as a round that recorded nothing. A whole bulk export
    /// would have declared every selected round empty. An unknown <c>args[0]</c> exits <b>64</b>,
    /// which is the one code a client can read as "this binary is too old" and fall back on.</para>
    /// <para><b>The keys arrive in a FILE</b>, as <c>--ask-local</c>'s prompt does, because a
    /// thousand of them would overflow a Windows command line. JSON:
    /// <c>[{"sessionId": "…", "stage": "CodeReview", "number": 1}]</c> — and every entry must carry
    /// all three. A key missing its number used to deserialise to the default and then be answered
    /// as a round that was never recorded, which turns a malformed request into a false statement
    /// about somebody's round. (Code round, codex.)</para>
    /// <para><b>And this mode never exits 64, whatever is wrong with the request</b> — the plan
    /// round found that, twice and independently. 64 is the client's signal to fall back to one
    /// spawn per round, because it is what an OLD binary answers to an argument it never heard of.
    /// A binary that DOES know the mode answering 64 to an unreadable keys file would have the
    /// client read a bad request as an old server: five hundred processes, and a successful-looking
    /// export hiding the corruption behind the path the fallback exists for. A malformed request is
    /// <see cref="BadRequest"/> instead, which no client retries.</para>
    /// </remarks>
    /// <summary>
    /// EX_DATAERR — the request itself was wrong, and asking again a different way will not help.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT 64. 64 means "this binary is too old for the mode" and is the one code the
    /// extension answers by falling back to one spawn per round; a request fault must never be able
    /// to look like that.
    /// </remarks>
    private const int BadRequest = 65;

    internal static int FindingsManyJson(string[] args)
    {
        var flags = Flags(args);
        var settings = Server.PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        var keysFile = flags.GetValueOrDefault("--keys-file", string.Empty);
        if (keysFile.Length == 0)
        {
            Note("--findings-many needs --keys-file <path> holding a JSON array of {session, stage, number}");

            return BadRequest;
        }

        List<Server.RoundKeyDto>? asked;
        try
        {
            asked = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(keysFile), Server.ServerJsonContext.Default.ListRoundKeyDto);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Note($"--findings-many could not read its keys file: {e.Message}");

            return BadRequest;
        }

        if (asked is null || asked.Count == 0)
        {
            Note("--findings-many was given no rounds to read");

            return BadRequest;
        }

        // The same ceiling the page's own window has. A caller asking for more than can be loaded is
        // asking about rounds it cannot be holding.
        if (asked.Count > Store.RoundsQuery.MaxLimit)
        {
            Note($"--findings-many takes at most {Store.RoundsQuery.MaxLimit} rounds; it was given {asked.Count}");

            return BadRequest;
        }

        // No database at all is a FAILED read, not a list of rounds that were never recorded. The
        // caller is asking about rounds it has just listed out of this file; answering "never heard
        // of them" would put `not recorded` beside every one of them in the export, which is a claim
        // rather than the truth. (Code round, codex.)
        if (!Store.RoundsQuery.DatabaseExists(settings.DataDir))
        {
            Note("--findings-many: there is no rounds database to read, so nothing is known about these rounds");

            return 74; // EX_IOERR — the database, not the rounds
        }

        // Every key, whole, before anything is read. An incomplete one is a bad request and says
        // so; answering it would put "not recorded" beside a round nobody actually asked about.
        // Every key, whole, before anything is read. An incomplete one is a bad request and says
        // so; answering it would put "not recorded" beside a round nobody actually asked about.
        if (asked.Exists(one => one.SessionId.Length == 0 || one.Stage.Length == 0 || one.Number < 0))
        {
            Note("--findings-many: every key needs a sessionId, a stage and a number of its own");

            return BadRequest;
        }

        try
        {
            var answer = Store.RoundsQuery.FindingsOfMany(
                settings.DataDir,
                [.. asked.Select(one => new Store.RoundKeyAsked(one.SessionId, one.Stage, one.Number))]);
            Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                answer, Server.ServerJsonContext.Default.LoggedManyFindings));
        }
        catch (Exception e) when (Unreadable(e) || e is IOException or UnauthorizedAccessException)
        {
            // The second half of that condition is the code round's: a database the process cannot
            // open for a reason SQLite never sees — a permission, a vanished directory — used to
            // leave this method by an unhandled exception, so the client read a crash code instead
            // of one of the three this mode documents.
            Note(Unreadable(e) ? WhyUnreadable(e) : e.Message);

            return 74; // EX_IOERR — the database, not the rounds
        }

        return 0;
    }

    private static int FindingsJson(string[] args)
    {
        var flags = Flags(args);
        var settings = Server.PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        try
        {
            var answer = Store.RoundsQuery.FindingsOf(
                settings.DataDir,
                flags.GetValueOrDefault("--session", string.Empty),
                flags.GetValueOrDefault("--stage", string.Empty),
                int.TryParse(flags.GetValueOrDefault("--number", string.Empty), out var number) ? number : -1);

            if (!answer.Known)
            {
                Note("no such round in the rounds database — its findings were never recorded.");
                return 69; // EX_UNAVAILABLE
            }

            Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                answer, Server.ServerJsonContext.Default.LoggedRoundFindings));
        }
        catch (Exception e) when (Unreadable(e))
        {
            Note(WhyUnreadable(e));
            return 74; // EX_IOERR — the database, not the round
        }

        return 0;
    }

    /// <summary>
    /// Whether this fault is the database being unreadable, rather than a fault of ours.
    /// </summary>
    /// <remarks>
    /// The missing native library belongs here and was not: it arrives as a TypeInitializationException
    /// wrapping DllNotFoundException, thrown from SQLite's type initializer, so the original filter
    /// never saw it and 0.18.1 answered `--log` with a stack trace. The one command a person runs to
    /// find out what happened must not be the one that dies.
    /// </remarks>
    internal static bool Unreadable(Exception e) =>
        Chain(e).Any(one => one is Microsoft.Data.Sqlite.SqliteException or IOException
            or UnauthorizedAccessException or DllNotFoundException);

    /// <summary>What to say about it — and, for the missing library, what to do about it.</summary>
    internal static string WhyUnreadable(Exception e) =>
        Missing(e) is { } absent
            ? $"the SQLite library ({absent}) is not beside the executable, so no round can be read or "
                + "recorded. Reinstall the server from the panel; a release archive carries it."
            : $"the rounds database could not be read: {e.Message}";

    /// <summary>The name the loader could not find, or nothing when this is a different fault.</summary>
    private static string? Missing(Exception e) =>
        Chain(e).Any(one => one is DllNotFoundException) ? "e_sqlite3" : null;

    /// <summary>
    /// An exception and every one it wraps.
    /// </summary>
    /// <remarks>
    /// The WHOLE chain, because the interesting fault is never on top and the layers between are not
    /// ours to predict. Measured on the published 0.18.2 binary with its library removed:
    /// <c>TypeInitializationException</c> wraps <c>TargetInvocationException</c> wraps
    /// <c>DllNotFoundException</c> - and the first version of this code knew only the first and the
    /// last, so it recursed into a type it did not recognise, stopped, and let the crash through. The
    /// test that was supposed to prove otherwise had built the chain by hand without the middle layer.
    /// </remarks>
    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (var one = e; one is not null; one = one.InnerException)
        {
            yield return one;
        }
    }

    /// <summary>
    /// `--log --limit 50`, or the default — and never outside the bounds a page has.
    /// </summary>
    /// <remarks>
    /// A number nobody can READ is the default; a number somebody can read but should not have is
    /// clamped rather than refused, because `--limit 100000` is a wish for as much as there is and
    /// an error would answer it with nothing. The plan round asked for this: the CLI is a boundary,
    /// and a boundary that trusts its input is not one.
    /// </remarks>
    internal static int Limit(string[] args, int fallback = Store.RoundsQuery.DefaultLimit)
    {
        var at = Array.IndexOf(args, "--limit");

        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var limit)
            ? Math.Clamp(limit, 1, Store.RoundsQuery.MaxLimit)
            : fallback;
    }

    /// <summary>
    /// `--before <cursor>`, or nothing — which asks for the first page.
    /// </summary>
    /// <remarks>
    /// The cursor is opaque here on purpose: it is `started_utc|id`, the pair the list is ordered
    /// by, and only <c>RoundsQuery</c> knows that. This checks that a value was given at all; a
    /// value it cannot use is treated there as absent, which asks for the first page.
    /// </remarks>
    internal static string Before(string[] args)
    {
        var at = Array.IndexOf(args, "--before");

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : string.Empty;
    }

    /// <summary>
    /// Whether the caller speaks the paged shape.
    /// </summary>
    /// <remarks>
    /// The compatibility hinge, and it is one flag. An extension that predates paging never sends
    /// it, so a NEW binary answers it exactly as it always did — findings inline, no totals — and
    /// nothing goes silently empty in the field. A NEW extension always sends it, so an OLD binary
    /// refuses the unknown argument with exit 64 and the extension retries without it.
    /// </remarks>
    internal static bool Paged(string[] args) => Array.IndexOf(args, "--paged") >= 0;

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
        var maxTokens = int.TryParse(flags.GetValueOrDefault("--max-tokens", ""), out var cap) && cap > 0
            ? cap
            : 8192;

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
                body = Runners.Reviewers.LocalAsk.RequestBody(model, prompt, schema, seed, reasoningEffort, maxTokens);
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

            // ONE CALLER AT A TIME on this engine, across every process on this machine. The
            // scheduler serialises the reviewers of one server, and this machine runs several MCP
            // clients at once, each with a server of its own — three requests on one card is what
            // turned a 30-second reviewer into two cancelled at 590 s.
            //
            // The wait is bounded by the SAME deadline the work has, so a queue cannot quietly eat a
            // reviewer's budget and then be reported as a slow engine. They are different problems
            // with different cures, and the messages say which one happened.
            var untilUtc = DateTime.UtcNow + deadline;
            var engineKey = Runners.Reviewers.LocalRuntime.EngineKey(endpoint);
            using var lease = await Runners.Reviewers.EngineLease.AcquireAsync(
                engineKey,
                untilUtc,
                // Built where it is RECOGNISED, never here. Composed inline, this sentence had
                // nothing tying it to whatever was supposed to tell it apart from a verdict — and
                // nothing did: on 2026-09-08 a round reported forty-five characters of it, cut
                // mid-word, in place of the cure printed directly underneath.
                (soFar, ahead) => Note(
                    Runners.Reviewers.LocalAsk.WaitingMessage(endpoint, ahead, soFar, model, Environment.ProcessId)));

            if (lease is null)
            {
                Note(Runners.Reviewers.LocalAsk.QueuedOutMessage(endpoint, deadline));

                return 69; // EX_UNAVAILABLE
            }

            // What is LEFT of the deadline, never the whole of it again: the wait was part of it.
            // And when nothing is left, the question is not asked at all — a five-second floor here
            // would run the reviewer PAST the deadline its executor is enforcing, which is the one
            // way this could report work nobody was waiting for any more. (codex, code round.)
            var remaining = untilUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                Note(Runners.Reviewers.LocalAsk.QueuedOutMessage(endpoint, deadline));

                return 69; // EX_UNAVAILABLE
            }

            var thinking = System.Diagnostics.Stopwatch.StartNew();
            using var http = new HttpClient { Timeout = remaining };
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"{endpoint.TrimEnd('/')}/chat/completions", content);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Note($"the local engine answered {(int)response.StatusCode}: {Trim(text)}");
                return 70;
            }

            // Recorded only for a run that WORKED. An endpoint answering 404 in three milliseconds is
            // not a three-millisecond run, and averaging it in would tell the next person queued that
            // their wait is nearly over. (gemini, code round.)
            lease.Record(engineKey, model, thinking.Elapsed);

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
            // A cancellation is only "too slow" when the deadline is what expired. The same
            // exception arrives when the round is abandoned or the client goes away, and claiming
            // the engine missed a deadline it never reached would be a confident lie — raised in
            // this change's code round.
            Note(ex switch
            {
                TaskCanceledException when waited.Elapsed >= deadline - TimeSpan.FromSeconds(1) =>
                    Runners.Reviewers.LocalAsk.TooSlowMessage(endpoint, waited.Elapsed, deadline),
                TaskCanceledException =>
                    $"this reviewer was cancelled after {waited.Elapsed.TotalSeconds:F0}s of the "
                        + $"{deadline.TotalSeconds:F0}s it was given — the round ended, or the client "
                        + "went away. The engine at " + endpoint + " was not asked to stop by us.",
                _ => Runners.Reviewers.LocalAsk.UnreachableMessage(endpoint, ex.Message),
            });

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
        using var log = ServiceDefaults.CoaiLogging.CreateDewFlowLogger(
            AppName,
            consoleToStdErr: true,
            // Beside the sessions and the database, not beside the binary: the data directory is the
            // one place a person is told about, and the binary's is inside the extension's storage.
            logsRoot: ServiceDefaults.CoaiLogPath.RootFor(
                SettingsFile.DataDirFrom(Environment.GetEnvironmentVariable)));
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

            // The same rule for the DATA DIRECTORY, and deliberately here rather than inside the
            // settings: these two notes are the only ones that have to look at the disk — is there a
            // database loose in a shared root, is this side's directory new — and a settings record
            // that stats a configured NAS would make `--version` hang on an unreachable mount rather
            // than answer. Raised on both code rounds (issue #115).
            foreach (var note in PanelSettings.StorageNotes(Environment.GetEnvironmentVariable))
            {
                log.Warning("data directory: {Note}", note);
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

            // And the one PROMPT, which is the person's own trigger: `/mcp__coai__consult` in a
            // client that lists them. A prompt calls nothing — it hands the assistant a message —
            // so every cap, refusal and invariant the tool has still applies to what it decides.
            options.PromptCollection ??= [];
            foreach (var prompt in Prompts.All())
            {
                options.PromptCollection.Add(prompt);
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
        It is ADDITIONAL: run your own review exactly as you would have, and start both AT ONCE. A
        different vendor's model answers what your own is worst placed to answer; your reviewers read
        the change in context. A `call_human` verdict stops the SHIPPING, not the task.
        The protocol, in order: `open` a session for the repo+branch. `review_plan` sends the plan to
        every enabled provider; `resolve` records an accept/reject for EVERY finding (a rejection needs
        a reason). Repeat until the verdict is `proceed`, implement, then `review_code` (three reviewers
        per provider), `resolve`, fix — the same loop. `review_code` REFUSES until a plan round reached
        `proceed`. `providers` says what is configured; `status` re-orients a resumed conversation;
        `ask_human` escalates to the person.
        Three things the tool descriptions say more about, and none of them is optional:
        1. A reply can carry COMMANDS — a `commands` list from the operator's panel switches, with a
           preamble. They outrank your own defaults and are about HOW to work. Say which you applied.
           An empty list is the default, not a fault.
        2. Reject in round ONE, not when the rounds run out. Accepting to be agreeable stops the loop
           converging: each accepted finding rewrites the plan, so the next round is handed fresh text
           and the count never falls.
        3. `call_human` is an ENFORCED stop. `review_plan` and `review_code` refuse while it stands and
           `resolve` does not clear it; only a person's answer does, and `humanDecision: "proceed"`
           applies ONLY after that verdict — so you cannot grant yourself the override.
        `consult` is for when YOU are stuck: an independent model reads this checkout read-only with its
        uncommitted diff and answers advice, not orders — verify it, then report back on the same
        consultationId. The person can ask for one themselves with the `consult` PROMPT.
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
        `--log --paged [--limit 200] [--before <cursor>]` prints a PAGE of the rounds database as
        JSON, with the totals SQL counted over the whole table; without `--paged` it prints the
        older shape, which carries every listed round's findings inline.
        `--findings --session <id> --stage <stage> --number <n>` prints one round's findings; it
        exits 69 when the database has never heard of that round, which is not the same as finding
        nothing.
        `--normalize --in <requests.json> --out <answers.json>` reads methods out of source and
        rewrites them so nothing of the project is left in them — the corpus collector's parser. It
        reads no source files itself; each request carries the text. Exits 66 when the request cannot
        be read and 73 when the answers cannot be written; a method it could not locate is an answer,
        not a failure.
        `--collect-bugs [--limit 200] [--all] [--model local/<name>]` decides what became of every unprocessed candidate — the fix
        commit, or the reason there is none — and writes it to the findings rows. Prints the funnel on
        stdout and its progress on stderr. `--all` revisits candidates an earlier run handled.
        `--bugs-json [--limit 200] [--all]` prints the accepted findings as corpus material, with the
        funnel that narrowed to them. `--all` includes the ones a collector run has already handled.
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
