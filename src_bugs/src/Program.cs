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

        // The ADMIN limit is its own setting, validated identically. One number cannot serve both
        // surfaces: the contributor default is flood control on a public endpoint, and using it here
        // makes the Users tab refuse itself after ten pages of a 250-page audit.
        var adminSurface = RatePerMinute.Surface.Administrator;
        var adminRate = RatePerMinute.Parse(
            Environment.GetEnvironmentVariable(adminSurface.Variable), adminSurface);
        if (adminRate is RatePerMinute.Parsed.Refused unusableAdminRate)
        {
            return new Startup.Refused(unusableAdminRate.Why);
        }

        // The administrators arrive BASE64 — one line, because a systemd EnvironmentFile assignment
        // cannot hold a newline and the list is newline-separated. An unusable value is refused here
        // rather than falling back to the raw text: the two shapes overlap, so a fallback would
        // start this server with the WRONG administrators and nothing would say so.
        var admins = AdminKeys.Read(Environment.GetEnvironmentVariable(AdminKeys.Variable), secret);
        if (admins is AdminKeys.Configured.Refused unusableAdmins)
        {
            return new Startup.Refused(unusableAdmins.Why);
        }

        var keywords = SkeletonKeywords.From(Keywords());

        return WhyUnusable(keywords) is { Length: > 0 } unusable
            ? new Startup.Refused(unusable)
            : new Startup.Ready(
                new ServerSecret(secret),
                keywords,
                ((RatePerMinute.Parsed.Rate)rate).Value,
                ((RatePerMinute.Parsed.Rate)adminRate).Value,
                ((AdminKeys.Configured.Admins)admins).Keys);
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
        if (BothAnAdminAndAKey(corpus, ready.Admins) is { Length: > 0 } wearingTwoHats)
        {
            return await RefuseAsync(wearingTwoHats);
        }

        var app = Built(args, ready);
        SayUp(app, ready, corpus);
        WatchTheEdge(app);
        var limiter = app.Services.GetRequiredService<RateLimiter>();
        var adminLimiter = app.Services.GetRequiredKeyedService<RateLimiter>(AdminLimiter);
        var gate = new IngestGate(corpus, ready.Admins, limiter, adminLimiter, ready.Secret);
        app.Use(gate.InvokeAsync);

        // Unauthenticated, and it says nothing about the corpus: a health probe that reported a count
        // would be an unauthenticated read of how much anybody has contributed. The limit is a
        // SETTING, not a fact about anybody, and an operator confirming a deploy wants to see it.
        // The admin gate is a PREFIX match on /admin, so a route added later is protected by
        // default rather than by somebody remembering to protect it.
        var admin = new AdminGate(ready.Admins, adminLimiter, ready.Secret);
        app.Use(admin.InvokeAsync);

        app.MapAdmin(corpus, limiter, adminLimiter, ready.Secret, app.Services.GetRequiredService<TimeProvider>());

        app.MapGet("/health", () => Results.Ok(new Health("ok")));

        app.MapPost(
            "/ingest",
            (UploadRequest? request, HttpContext http, TimeProvider clock) =>
                Judged(corpus, ready.Keywords, request, IngestGate.WhoOf(http), clock, limiter));

        // THE ROUTE IS THE MECHANISM, and it is why there is no capability probe.
        //
        // A comment must never be dropped in silence, and a server older than 2026-09-21 would drop
        // one: `BugsJson` declares only a naming policy, so System.Text.Json skips a member it does
        // not know and the pair is accepted without it. Asking such a server whether it takes
        // comments cannot help — the answer and the POST can reach different nodes during a rollout,
        // and by the time the client learns anything the comment is gone and a retry is answered
        // `duplicate`. (Plan round, codex.)
        //
        // So a comment travels on a route an old binary does not HAVE. Its refusal is a 404: nothing
        // written, nothing lost, and no ordering of deployments can produce a window. `/ingest` is
        // untouched — same route, same behaviour, and byte-identical requests for a batch with no
        // comment in it.
        app.MapPost(
            "/ingest/commented",
            (UploadRequest? request, HttpContext http, TimeProvider clock) =>
                Judged(corpus, ready.Keywords, request, IngestGate.WhoOf(http), clock, limiter,
                    commented: true));

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

        // A SECOND limiter for the admin surface, keyed to its own setting. Sharing one instance
        // would give one window per subject but one LIMIT for both, which is the thing the separate
        // setting exists to avoid.
        builder.Services.AddKeyedSingleton(AdminLimiter, (IServiceProvider services, object _) =>
            new RateLimiter(ready.AdminRate, services.GetRequiredService<TimeProvider>()));
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
                + "{Rate} requests a minute per key (0 = no limit), {Admins} administrators at "
                + "{AdminRate} a minute",
                ready.Keywords.Count,
                corpus.WaitingCount(),
                corpus.Held(),
                ready.Rate.Value,
                ready.Admins.Count,
                ready.AdminRate.Value);
        }

        // The ONE place the operator can learn this, and the reason it is a separate line at Warning.
        // `/admin/*` answers 401 to everybody when no administrator is configured, and it answers it
        // identically to a wrong credential — deliberately, so the endpoint is not an oracle for
        // whether administration is enabled here. That disclosure rule is what leaves the operator
        // with no way to tell a missing variable from their own typo, so the server says it on the
        // host, where only they can read it. `AdminKeys` and `AdminGate` both promise this line.
        if (ready.Admins.None)
        {
            app.Logger.LogWarning(
                "no administrators are configured: {Variable} is absent or holds no key, so every "
                + "/admin request will be refused. That is a legitimate way to run this server; if "
                + "it is not what you meant, the variable is the place to look",
                AdminKeys.Variable);
        }
    }

    /// <summary>
    /// Why a configured administrator cannot also be a contributor key, or empty when none is.
    /// </summary>
    /// <remarks>
    /// <para><b>The two credential stores are asked in order, so one string in both is a caller
    /// whose identity depends on which store still holds it.</b> It uploads as a contributor —
    /// counted against its key row, limited by the contributor setting — and the moment that row is
    /// revoked the same bearer string becomes an administrator: uncounted, limited by the other
    /// setting, and able to issue keys. A revocation that PROMOTES a credential is the opposite of
    /// what the operator pressed the button for. (Code round, codex.)</para>
    /// <para><b>Refused rather than resolved by a precedence rule</b>, because both precedences are
    /// wrong in one direction: contributor-first is the surprise above, and admin-first means
    /// pasting an issued contributor key into the variable silently grants administration. There is
    /// no legitimate reason for one string to be both, so this is a configuration error — it exits
    /// 78, like an unusable rate limit, and names the variable to edit.</para>
    /// <para>It needs the database open AND the variable parsed, which is why it lives here rather
    /// than in <see cref="Configure"/>: those two halves only exist together once the corpus is
    /// open. It costs one indexed lookup per configured administrator, once, at startup.</para>
    /// </remarks>
    private static string BothAnAdminAndAKey(Corpus corpus, AdminKeys admins)
    {
        foreach (var hash in admins.Hashes)
        {
            if (corpus.KeyWithHash(hash) is { Length: > 0 } key)
            {
                return $"a line in {AdminKeys.Variable} is also the contributor key {key}. One "
                    + "string cannot be both: it would upload as a contributor until that key was "
                    + "revoked and become an administrator afterwards, so revoking it would GRANT "
                    + "administration. Remove that line, or revoke and reissue the contributor key.";
            }
        }

        return string.Empty;
    }

    /// <summary>The key the ADMIN limiter is registered under, so it cannot be resolved by accident.</summary>
    /// <remarks>
    /// Two <see cref="RateLimiter"/> instances live in the container and they are not
    /// interchangeable: one holds the contributor limit and one the administrator limit. A keyed
    /// registration makes asking for the wrong one a compile-time choice rather than a silent one.
    /// </remarks>
    private const string AdminLimiter = "coai-bugs.admin-limiter";

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
    /// <param name="commented">
    /// Whether the request arrived on <c>/ingest/commented</c>. Only that route reads a comment; a
    /// pair posted to <c>/ingest</c> is stored without one however it was serialised, so the two
    /// routes really do differ and a test asserts exactly that.
    /// </param>
    private static Answered Judged(
        Corpus corpus,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        UploadRequest? request,
        Uploader uploader,
        TimeProvider clock,
        RateLimiter limiter,
        bool commented = false)
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

        return Taken(corpus, uploader, UtcMonth.Now(clock), limiter, scope => Ingest.Take(scope, items, keywords, commented));
    }

    /// <summary>Stores the batch down the path its credential belongs to.</summary>
    /// <remarks>
    /// <para>The two are not interchangeable, which is why the gate hands over a
    /// <see cref="Uploader"/> and not a string. A contributor's batch counts against a key row and
    /// can still be refused inside the transaction — a `--revoke` in another process between the
    /// gate and the write — so it answers a union. An administrator has no row to count and no set
    /// that can change while the process lives, so <see cref="Corpus.AcceptAdmin"/> has nothing to
    /// refuse and answers the batch.</para>
    /// <para>The month is computed ONCE, above, and passed to whichever path runs: reading the clock
    /// twice is how a batch on the stroke of midnight gets stamped with two different months.</para>
    /// </remarks>
    private static Answered Taken(
        Corpus corpus,
        Uploader uploader,
        UtcMonth month,
        RateLimiter limiter,
        Func<IngestScope, UploadAnswer> take) =>
        uploader switch
        {
            Uploader.Contributor contributor =>
                Answer(corpus.Accept(contributor.Key, month, take), contributor.Key, limiter),
            Uploader.Administrator administrator =>
                TypedResults.Ok(corpus.AcceptAdmin(administrator.Id, month, take)),
            _ => throw new InvalidOperationException(
                "the gate answers 401 to nobody; an unauthenticated request never reaches here"),
        };

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

        limiter.Forgive(LimiterSubject.Contributor(key));

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
            RatePerMinute Rate,
            RatePerMinute AdminRate,
            AdminKeys Admins) : Startup;

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
/// <para><b>Nothing about the corpus either.</b> A count here would be an unauthenticated read of
/// how much anybody has contributed.</para>
/// </remarks>
public sealed record Health(string Status);

/// <summary>Why a request was refused, in words rather than a code.</summary>
public sealed record Problem(string Why);

/// <summary>The serializer's whole world: nothing reflective, as every binary here is built.</summary>
/// <remarks>
/// <b>The naming policy belongs HERE, not at the call sites.</b> A route's
/// <c>TypedResults.BadRequest</c> is written through the host's JSON options, which camel-case
/// property names; a gate writes its body with one of this context's <c>JsonTypeInfo</c> objects
/// directly, which uses THIS context's options. With none declared, the same <see cref="Problem"/>
/// left the server as `why` from a route and `Why` from a gate, so a client reading errors needed
/// two spellings for one server. Declaring it on the context makes the two paths agree by
/// construction, and a test reads the bytes of all three refusal paths.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UploadRequest))]
[JsonSerializable(typeof(UploadAnswer))]
[JsonSerializable(typeof(Health))]
[JsonSerializable(typeof(Problem))]
[JsonSerializable(typeof(AdminWire.KeysPage))]
[JsonSerializable(typeof(AdminWire.Issued))]
[JsonSerializable(typeof(AdminWire.Revocation))]
[JsonSerializable(typeof(AdminWire.AuditPage))]
[JsonSerializable(typeof(AdminWire.ActiveNow))]
[JsonSerializable(typeof(AdminWire.IssueRequest))]
internal sealed partial class BugsJson : JsonSerializerContext;
