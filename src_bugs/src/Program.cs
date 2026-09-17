using System.Reflection;
using System.Text.Json.Serialization;
using CoaiMcp.Core.Collecting;
using CoaiMcp.ServiceDefaults;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Serilog;
using Answered = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Ok<CoaiMcp.Core.Collecting.UploadAnswer>,
    Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult,
    Microsoft.AspNetCore.Http.HttpResults.BadRequest<CoaiBugs.Problem>>;

namespace CoaiBugs;

/// <summary>
/// `coai-bugs`: where anonymous before/after pairs arrive, and the only thing here that is public.
/// </summary>
/// <remarks>
/// <para><b>A binary of its own, not an endpoint on `coai-server`.</b> The operator's decision on
/// 2026-09-15: that server sits behind Entra and a domain allow-list, which is load-bearing there and
/// exactly wrong here — anyone should be able to contribute, not only people with a Team server.</para>
/// <para><b>About a contributor it records almost nothing, and no table that carries a key id carries
/// a clock time.</b> A key grants write access and carries no identity: the key table has no name
/// and no address, and of WHEN a key was used it holds only the calendar month it was last used —
/// no date, no clock time, no history — plus a lifetime count. A pair in quarantine carries the month
/// it arrived, beside its key id; its <c>received_utc</c> column is frozen in step 1 and written
/// empty for ever. The route logs no client address, which this process cannot guarantee on its own
/// — see `IngestGate` and the deploy notes.</para>
/// <para><b>About administrators it records exact times</b>: who issued or revoked a key, and when.
/// That is a log about the people holding administrative power, not about the people contributing.
/// <c>corpus.promoted_utc</c> is exact too, and may be: <c>corpus</c> carries no key id, so a
/// promotion is a person's decision about a pair, attributable to nobody who contributed.</para>
/// </remarks>
// NOT static: `WebApplicationFactory<Program>` takes it as a type argument, which is how the
// tests host this server in-process — and a static class cannot be a type argument at all.
//
// IN A NAMESPACE, unlike `coai-server`'s equivalent. A type at global scope is a name every
// assembly referencing this one has to live with, and `Program`, `Health` and `Problem` are three
// of the most collidable names there are. Sonar rates it a reliability BUG (S3903) and it is right.
// The server's own `Program.cs` has the same shape and is NOT changed here: it is outside this
// story, it is load-bearing for that binary's test harness, and rewriting a neighbour uninvited is
// how a small change becomes a large diff. It is worth doing, and it is worth asking first.
internal sealed class Program
{
    // S1118: every member here is static, so nothing should ever construct one. Private rather
    // than making the class static, because `WebApplicationFactory<Program>` needs a type it can
    // name as a type argument.
    private Program()
    {
    }

    /// <summary>The environment variable holding the secret keys are hashed with.</summary>
    /// <remarks>
    /// Not a file and not an argument: an argument is in process listings and shell history. Without
    /// it the server refuses to start, because hashing with a default secret is hashing with no
    /// secret at all.
    /// </remarks>
    private const string SecretVariable = "COAI_BUGS_SECRET";

    /// <summary>The database, under the data directory, beside `logs/`.</summary>
    internal const string DatabaseFileName = "coai-bugs.db";

    private static async Task<int> Main(string[] args)
    {
        // One-shot admin modes, chosen from args[0] before any transport is opened — the shape
        // `.agents/PROJECT.md` sanctions, and the reason a fresh deployment is not an empty table
        // with no documented way to fill it. (Plan round, codex.)
        //
        // `Admin.Knows` rather than a second copy of the list: this predicate and the switch that
        // dispatches were two hand-maintained lists, and a mode added to one but not the other
        // either starts Kestrel instead of running the one-shot, or falls through the switch's
        // default and silently runs `--waiting`. Both are silent. (Code round, codex.)
        if (Admin.Knows(args))
        {
            return Admin.Run(args, Secret(), Data(), TimeProvider.System);
        }

        // AND THE OTHER HALF OF THE RULE, which this binary did not have at all: an argument that
        // names no mode must exit 64, because 64 is how a caller learns it is talking to a binary
        // too old for what it asked. `coai-bugs --rotate-the-moon` started Kestrel and listened
        // for ever — a scenario test over the real executable found it, four minutes and
        // fifty-seven seconds in, which is exactly how long it takes to notice that a process
        // nobody asked to start is still running.
        if (Unknown(args) is { Length: > 0 } unknown)
        {
            await Console.Error.WriteLineAsync(
                $"[coai-bugs] '{unknown}' is not a mode this binary has. Modes: "
                + $"{string.Join(", ", Admin.Modes)}. With no mode it serves /ingest.");

            return 64; // EX_USAGE
        }

        return await ServeAsync(args);
    }

    /// <summary>
    /// Everything after the one-shot modes: the configuration, then the transport.
    /// </summary>
    /// <remarks>
    /// Several small methods rather than one so that none exceeds the cyclomatic bound of four the
    /// C# doctrine sets: <see cref="Configure"/> reads and refuses, this decides, and
    /// <see cref="ListenAsync"/> takes what must be held and serves. `Main` is three decisions — is
    /// this an admin mode, is this a mode at all, otherwise serve — which is also the clearest
    /// statement of what this binary does.
    /// </remarks>
    private static async Task<int> ServeAsync(string[] args) => Configure() switch
    {
        Startup.Refused refused => await RefuseAsync(refused.Why),
        Startup.Ready ready => await ListenAsync(args, ready),
        var other => throw new InvalidOperationException($"{other.GetType().Name} is not a startup outcome"),
    };

    /// <summary>
    /// Reads every setting the server needs, or says which one it cannot start with.
    /// </summary>
    /// <remarks>
    /// <para>BEFORE the database is opened, and as a VALUE rather than an exception. The C# doctrine
    /// says expected failures are values and never `throw` for control flow, and a setting that
    /// parsed to nothing usable is an expected configuration result. The keyword check used to run
    /// AFTER `Corpus.Open`, so a misconfigured server created its database file on the way to
    /// refusing to start. (Code round, codex/local.)</para>
    /// <para>78 rather than an unhandled exception, because the deploy unit sets
    /// `RestartPreventExitStatus=78`: a permanent configuration fault must stop, and `Restart=always`
    /// would otherwise restart the same broken binary every five seconds for ever while the operator
    /// reads a crash loop instead of one clear line. (Code round, codex.)</para>
    /// </remarks>
    private static Startup Configure()
    {
        var secret = Secret();
        if (secret.Length == 0)
        {
            return new Startup.Refused(
                $"{SecretVariable} is not set. Keys are hashed with it, so starting without one "
                + "would mean hashing with no secret at all.");
        }

        // The rate limit: validated once, here, and refused loudly past its cap — an unvalidated
        // rate times the caller ceiling is a billion timestamps in memory.
        var rate = RatePerMinute.Parse(Environment.GetEnvironmentVariable(RatePerMinute.Variable));
        if (rate is RatePerMinute.Parsed.Refused unusableRate)
        {
            return new Startup.Refused(unusableRate.Why);
        }

        var keywords = SkeletonKeywords.From(Keywords());

        return WhyUnusable(keywords) is { Length: > 0 } unusable
            ? new Startup.Refused(unusable)
            : new Startup.Ready(new ServerSecret(secret), keywords, ((RatePerMinute.Parsed.Rate)rate).Value);
    }

    private static async Task<int> RefuseAsync(string why)
    {
        await Console.Error.WriteLineAsync($"[coai-bugs] {why}");

        return 78; // EX_CONFIG
    }

    /// <summary>The transport: one server per data directory, one connection, two routes.</summary>
    /// <remarks>
    /// <para><b>The lock FIRST, before a logger or a listener is configured</b> — a server that will
    /// not serve should build nothing — and the database next, so a file that is not a database
    /// refuses here with its reason rather than as a stack trace. Both are held for the life of the
    /// host through <c>using</c>; a throw anywhere below releases them.</para>
    /// <para>The order of the request pipeline is the contract: the edge watch sees every request,
    /// the <see cref="IngestGate"/> answers 401 and 429 BEFORE any body is read, and only a request
    /// that passed both reaches the endpoint, where the body is bound and judged.</para>
    /// </remarks>
    private static async Task<int> ListenAsync(string[] args, Startup.Ready ready)
    {
        var taken = ServeLock.Take(Data());
        if (taken is ServeLock.Taken.Refused refusedLock)
        {
            return await RefuseAsync(refusedLock.Why);
        }

        using var serving = ((ServeLock.Taken.Held)taken).Lock;
        var opened = Opened(Path.Combine(Data(), DatabaseFileName));
        if (opened is Database.Refused refusedDb)
        {
            return await RefuseAsync(refusedDb.Why);
        }

        using var corpus = ((Database.Ready)opened).Corpus;
        var app = Built(args, ready);
        SayUp(app, ready, corpus);
        WatchTheEdge(app);
        var gate = new IngestGate(corpus, app.Services.GetRequiredService<RateLimiter>(), ready.Secret);
        app.Use(gate.InvokeAsync);

        // Unauthenticated, and it says nothing about the corpus: a health probe that reported a count
        // would be an unauthenticated read of how much anybody has contributed. The limit is a
        // SETTING, not a fact about anybody, and an operator confirming a deploy wants to see it.
        app.MapGet("/health", () => Results.Ok(new Health("ok")));

        app.MapPost(
            "/ingest",
            (UploadRequest? request, HttpContext http, TimeProvider clock, RateLimiter limiter) =>
                Judged(corpus, ready.Keywords, request, IngestGate.KeyOf(http), clock, limiter));

        await app.RunAsync();

        return 0;
    }

    /// <summary>Everything the host is made of, before anything listens.</summary>
    private static WebApplication Built(string[] args, Startup.Ready ready)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        // Two sinks, always, the same bootstrap `coai-server` calls — colour to the console and one
        // file per run under the data directory's `logs/{yyyy-MM-dd}/`. It was missing entirely, and
        // a server whose only record of a run is a terminal buffer has no record of it.
        //
        // What is deliberately NOT wired is `UseSerilogRequestLogging`: request logging writes the
        // client address, which is the one thing this server promises never to keep.
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(
            CoaiLogging.CreateDewFlowLogger("coai-bugs", logsRoot: CoaiLogPath.RootFor(Data())),
            dispose: true);

        builder.Services.ConfigureHttpJsonOptions(
            options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, BugsJson.Default));
        builder.WebHost.ConfigureKestrel(
            kestrel => kestrel.Limits.MaxRequestBodySize = Ingest.MostBytes);

        // The clock is a SERVICE, so the month a key is stamped with and the limiter's window come
        // from one injectable source: the test harness swaps it for a frozen one, which is how a
        // test sits either side of a month boundary. Nothing that writes a row reads the machine.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(services =>
            new RateLimiter(ready.Rate, services.GetRequiredService<TimeProvider>()));
        builder.Services.AddHostedService<LimiterSweep>();

        return builder.Build();
    }

    /// <summary>Opens the database, or says why the server cannot start on it.</summary>
    /// <remarks>
    /// A file that is not a database — truncated, overwritten, a stray byte — surfaced as an unhandled
    /// <see cref="SqliteException"/> and a stack trace, which under `Restart=always` is a crash loop
    /// with the reason scrolling past every five seconds. It is one sentence and 78 now: the fault is
    /// permanent, and the unit does not restart on 78.
    /// </remarks>
    private static Database Opened(string path)
    {
        try
        {
            return new Database.Ready(Corpus.Open(path));
        }
        catch (SqliteException e)
        {
            return new Database.Refused(
                $"{path} cannot be opened as this server's database: {e.Message} (SQLite error "
                + $"{e.SqliteErrorCode}). Nothing here can repair it; restore it from the backup or "
                + "move it aside.");
        }
    }

    private static void SayUp(WebApplication app, Startup.Ready ready, Corpus corpus)
    {
        // Guarded, because two of those arguments are COUNT queries: an installation that has
        // turned Information off would pay for them anyway, on every start, to build a line nobody
        // reads. (SonarCloud CA1873.)
        if (app.Logger.IsEnabled(LogLevel.Information))
        {
            app.Logger.LogInformation(
                "coai-bugs is up: {Languages} keyword lists, {Waiting} waiting, {Held} in the corpus, "
                + "{Rate} requests a minute per key (0 = no limit)",
                ready.Keywords.Count, corpus.WaitingCount(), corpus.Held(), ready.Rate.Value);
        }
    }

    /// <summary>The forwarding-header watch, BEFORE routing, so it sees every request.</summary>
    /// <remarks>
    /// It lived inside the `/ingest` handler and two reviewers said the same thing: a header
    /// arriving at `/health`, at an unknown path, or at any route added later would never be
    /// noticed, and every new endpoint would have to remember to repeat the check. A cross-cutting
    /// property belongs at the boundary it is a property of. (Code round, gemini/codex.)
    /// </remarks>
    private static void WatchTheEdge(WebApplication app) =>
        app.Use(async (context, next) =>
        {
            if (EdgeSentAnAddress(context.Request) is { Length: > 0 } header)
            {
                // The NAME, never the value: a warning quoting the address would be the leak it
                // exists to report, written by the code that noticed it. DEPLOY.md rather than the
                // repository path, because that is what the release archive unpacks it as and the
                // operator reading this line has the archive, not the checkout. (Code round,
                // gemini/local.)
                app.Logger.LogWarning(
                    "the edge sent {Header}. This server promises not to hold contributor addresses "
                    + "and its vhost must clear the forwarding headers — see DEPLOY.md beside this "
                    + "binary, or deploy/bugs/README.md in the repository",
                    header);
            }

            await next(context);
        });

    /// <summary>
    /// The body judged, and — only then — the key's row moved with it, in one commit.
    /// </summary>
    /// <remarks>
    /// The key arrives from the <see cref="IngestGate"/>, which answered 401 and 429 before the body
    /// was read; what is left to decide here is the body (400) and the work. The key is checked once
    /// more INSIDE the write transaction, because a revoke can commit between the gate and the write.
    /// </remarks>
    private static Answered Judged(
        Corpus corpus,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        UploadRequest? request,
        KeyId key,
        TimeProvider clock,
        RateLimiter limiter)
    {
        if (request?.Items is not { } items)
        {
            return TypedResults.BadRequest(new Problem("the request has no `items` list"));
        }

        if (items.Count > Ingest.MostPerBatch)
        {
            return TypedResults.BadRequest(
                new Problem($"a batch carries at most {Ingest.MostPerBatch} pairs"));
        }

        var accepted = corpus.Accept(key, UtcMonth.Now(clock), scope => Ingest.Take(scope, items, keywords));

        return Answer(accepted, key, limiter);
    }

    /// <summary>
    /// The per-item results — or the 401 a key revoked between the gate and the write is owed.
    /// </summary>
    /// <remarks>
    /// Nothing was written and nothing counted for a key found not in force, and the stamp the gate
    /// granted is given back: the request did nothing, so it costs the key nothing.
    /// </remarks>
    private static Answered Answer(Accepted<UploadAnswer> accepted, KeyId key, RateLimiter limiter)
    {
        if (accepted is Accepted<UploadAnswer>.Stored stored)
        {
            return TypedResults.Ok(stored.Answer);
        }

        limiter.Forgive(LimiterSubject.Contributor(key.Value));

        return TypedResults.Unauthorized();
    }

    /// <summary>
    /// Arguments the HOST understands, which are not modes and are not this binary's to refuse.
    /// </summary>
    /// <remarks>
    /// The list is short because the surface is: a deployment says where to listen and which
    /// environment it is, and everything else about this server is an environment variable. Anything
    /// outside it that starts with <c>--</c> is a mode nobody here has heard of.
    /// </remarks>
    private static readonly string[] HostArguments =
        ["--urls", "--environment", "--contentRoot", "--applicationName"];

    /// <summary>The first argument that names nothing, or empty when they all name something.</summary>
    internal static string Unknown(string[] args)
    {
        if (args.Length == 0 || !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // An admin mode is KNOWN here too, even though `Main` takes it first. It is safe only
        // because of that order, and an order is an invisible dependency: moving these two checks
        // would make `--issue-key` exit 64 and tell every caller this binary is too old for a mode
        // it has. A test asked for this and it was right to.
        if (Admin.Knows(args))
        {
            return string.Empty;
        }

        // `--urls=x` and `--urls x` are the same argument to the host, so the name is what is in
        // front of any `=`.
        var name = args[0].Split('=', 2)[0];

        return Array.IndexOf(HostArguments, name) >= 0 ? string.Empty : args[0];
    }

    /// <summary>
    /// The headers an edge uses to pass a client's address on, none of which may reach this server.
    /// </summary>
    /// <remarks>
    /// Five rather than two: the Team server's vhost sets `X-Real-IP` and `X-Forwarded-For`, and a
    /// deployment that ever sits behind Cloudflare or a distro's stock `proxy_params` acquires the
    /// others without anybody choosing them. Naming only the two we write ourselves would watch for
    /// the mistake we already know about and miss the one we inherit.
    /// </remarks>
    /// <remarks>
    /// <b>Internal, because the tests read THIS rather than retyping it</b> — a list a test repeats
    /// is a list that will not notice its sixth entry — and because one of them asserts that every
    /// name here is also cleared in `deploy/nginx/coai-bugs`. The two halves of this boundary are a
    /// C# array and an nginx file, and nothing but that test holds them together. (Code round,
    /// codex.)
    /// </remarks>
    internal static readonly System.Collections.Frozen.FrozenSet<string> Forwarding =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(
            ["X-Forwarded-For", "X-Real-IP", "Forwarded", "CF-Connecting-IP", "True-Client-IP"],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The forwarding header this request carries, by NAME, or empty.</summary>
    /// <remarks>
    /// It answers the name and never the value, and that is the whole contract: the value is the
    /// thing being protected, so a report carrying it would defeat the report.
    /// </remarks>
    internal static string EdgeSentAnAddress(HttpRequest http)
    {
        // The REQUEST'S headers are walked, not the catalog: a request carries a handful and the
        // probe is a frozen-set hit rather than five dictionary look-ups through a closure. It runs
        // on every request now that it is middleware, which is what made this worth doing at all.
        // (Code round, gemini.)
        foreach (var header in http.Headers)
        {
            if (Forwarding.TryGetValue(header.Key, out var known))
            {
                // The catalog's spelling, not the request's: a header arriving as `x-real-ip`
                // must be reported by the name the vhost and the tests use.
                return known;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The keyword list, refused when it parsed to nothing usable.
    /// </summary>
    /// <remarks>
    /// <c>/health</c> answers from a route that touches neither this list nor the database, so a
    /// build accident that embedded an empty file would start, satisfy the release smoke, publish,
    /// and then refuse every submission with "this server has no keyword list for CSharp" — which
    /// reads like the contributor's fault and is not. Better to not start. (Plan round, local.)
    /// </remarks>
    /// <returns>Why the list is unusable, or empty when it is usable.</returns>
    internal static string WhyUnusable(IReadOnlyDictionary<string, IReadOnlySet<string>> keywords)
    {
        if (keywords.Count == 0)
        {
            return "no keyword list was parsed at all; the alphabet check is made of it and this "
                   + "server cannot validate a single pair without one";
        }

        foreach (var (language, words) in keywords)
        {
            if (words.Count == 0)
            {
                return $"the keyword list for {language} parsed to no words, so every {language} "
                       + "pair would be refused as if the contributor had leaked something";
            }
        }

        return string.Empty;
    }

    private static string Secret() =>
        Environment.GetEnvironmentVariable(SecretVariable) ?? string.Empty;

    private static string Data() =>
        Environment.GetEnvironmentVariable("COAI_BUGS_DATA") ?? Directory.GetCurrentDirectory();

    /// <summary>
    /// The keyword list, out of this binary rather than off the disk around it.
    /// </summary>
    /// <remarks>
    /// <para><b>It walked parent directories for `shared/skeleton-keywords.txt`, and FOUR reviewers
    /// noticed the same thing: a published AOT binary lives in a directory with no repository above
    /// it.</b> The startup then threw <c>FileNotFoundException</c> before the server listened, so a
    /// deployment that built perfectly could not run at all. The file is an embedded resource now —
    /// there is nothing beside the binary to lose, and the build fails if it is missing.</para>
    /// <para><c>COAI_BUGS_KEYWORDS</c> is read FIRST and REPLACES the embedded list — it is an
    /// override, not an alternative path to the same words. It exists for an operator who has to
    /// correct the list on a running deployment without waiting for a release; the embedded copy is
    /// what every ordinary start uses.</para>
    /// </remarks>
    internal static string Keywords()
    {
        if (Environment.GetEnvironmentVariable("COAI_BUGS_KEYWORDS") is { Length: > 0 } file)
        {
            return File.ReadAllText(file);
        }

        var assembly = typeof(Program).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"{ResourceName} is not embedded in this binary; it is the word list the alphabet "
                + "check is made of and the server cannot validate without it");
        using var text = new StreamReader(stream);

        return text.ReadToEnd();
    }

    /// <summary>What the embedded keyword file is called inside the assembly.</summary>
    internal const string ResourceName = "CoaiBugs." + SkeletonKeywords.FileName;

    /// <summary>What reading the configuration came to.</summary>
    private abstract record Startup
    {
        private Startup()
        {
        }

        /// <summary>Everything the server needs, read and validated.</summary>
        public sealed record Ready(
            ServerSecret Secret,
            IReadOnlyDictionary<string, IReadOnlySet<string>> Keywords,
            RatePerMinute Rate) : Startup;

        /// <summary>One setting the server will not start with, and the sentence to print.</summary>
        public sealed record Refused(string Why) : Startup;
    }

    /// <summary>The database, opened — or the sentence saying why the server cannot start on it.</summary>
    private abstract record Database
    {
        private Database()
        {
        }

        public sealed record Ready(Corpus Corpus) : Database;

        public sealed record Refused(string Why) : Database;
    }
}

/// <summary>What `/health` says: that the server is up, and the limit it was configured with.</summary>
/// <remarks>Nothing about the corpus — a count here would be an unauthenticated read of how much anybody has contributed.</remarks>
/// <summary>What <c>/health</c> answers: that the server is up, and deliberately nothing else.</summary>
/// <remarks>
/// <para><b>The configured rate limit was added here and taken out again.</b> A code-round finding
/// asked for the limit to be visible, and it is a fair problem — an operator could not see it — but
/// this endpoint is the wrong place to answer it. <c>/health</c> is unauthenticated and faces the
/// public internet, and the same reasoning already kept the VERSION out of it: a banner here tells
/// anybody what is running. A published rate limit is worse than a version, because it tells a
/// flooder exactly how fast it may go without ever being refused.</para>
/// <para>Where the limit IS visible: the startup log names it on every boot
/// (<c>"{Rate} requests a minute per key (0 = no limit)"</c>), the journal is where an operator
/// already looks, and story 2's authenticated <c>/admin/*</c> is where a UI will read it. The
/// existing test that caught this — <c>HealthSaysNothingAboutTheCorpus</c> — was the messenger.</para>
/// </remarks>
public sealed record Health(string Status);

/// <summary>Why a request was refused, in words rather than a code.</summary>
public sealed record Problem(string Why);

/// <summary>The serializer's whole world: nothing reflective, as every binary here is built.</summary>
[JsonSerializable(typeof(UploadRequest))]
[JsonSerializable(typeof(UploadAnswer))]
[JsonSerializable(typeof(Health))]
[JsonSerializable(typeof(Problem))]
internal sealed partial class BugsJson : JsonSerializerContext;
