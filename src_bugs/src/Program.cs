using System.Reflection;
using System.Text.Json.Serialization;
using CoaiMcp.Core.Collecting;
using CoaiMcp.ServiceDefaults;
using Microsoft.AspNetCore.Http.HttpResults;
using Serilog;

namespace CoaiBugs;

/// <summary>
/// `coai-bugs`: where anonymous before/after pairs arrive, and the only thing here that is public.
/// </summary>
/// <remarks>
/// <para><b>A binary of its own, not an endpoint on `coai-server`.</b> The operator's decision on
/// 2026-09-15: that server sits behind Entra and a domain allow-list, which is load-bearing there and
/// exactly wrong here — anyone should be able to contribute, not only people with a Team server.</para>
/// <para><b>It knows nothing about anybody.</b> A key grants write access and carries no identity; the
/// key table has no name, no address and no <c>last_seen_utc</c>; and the route logs no client
/// address. That last one this process cannot guarantee on its own — see the note on `/ingest`.</para>
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
            return Admin.Run(args, Secret(), Data());
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
    /// Everything after the one-shot modes: the transport, and the two things it needs first.
    /// </summary>
    /// <remarks>
    /// Its own method so that neither half exceeds the cyclomatic bound of four the C# doctrine
    /// sets. `Main` is now three decisions — is this an admin mode, is this a mode at all, otherwise
    /// serve — which is also the clearest statement of what this binary does.
    /// </remarks>
    private static async Task<int> ServeAsync(string[] args)
    {
        var secret = Secret();
        if (secret.Length == 0)
        {
            await Console.Error.WriteLineAsync(
                $"[coai-bugs] {SecretVariable} is not set. Keys are hashed with it, so starting "
                + "without one would mean hashing with no secret at all.");

            return 78; // EX_CONFIG
        }

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

        // BEFORE the database is opened, and as a VALUE rather than an exception.
        //
        // Two findings in one line. The C# doctrine says expected failures are values and never
        // `throw` for control flow, and a keyword list that parsed to nothing is an expected
        // configuration result. And it used to run AFTER `Corpus.Open`, so a misconfigured server
        // created its database file on the way to refusing to start. (Code round, codex/local.)
        //
        // 78 rather than an unhandled exception, because the deploy unit sets
        // `RestartPreventExitStatus=78`: a permanent configuration fault must stop, and
        // `Restart=always` would otherwise restart the same broken binary every five seconds for
        // ever while the operator reads a crash loop instead of one clear line. (Code round, codex.)
        var keywords = SkeletonKeywords.From(Keywords());
        if (WhyUnusable(keywords) is { Length: > 0 } unusable)
        {
            await Console.Error.WriteLineAsync($"[coai-bugs] {unusable}");

            return 78; // EX_CONFIG
        }

        var app = builder.Build();
        using var corpus = Corpus.Open(Path.Combine(Data(), "coai-bugs.db"));

        // Guarded, because two of those three arguments are COUNT queries: an installation that has
        // turned Information off would pay for them anyway, on every start, to build a line nobody
        // reads. (SonarCloud CA1873.)
        if (app.Logger.IsEnabled(LogLevel.Information))
        {
            app.Logger.LogInformation(
                "coai-bugs is up: {Languages} keyword lists, {Waiting} waiting, {Held} in the corpus",
                keywords.Count, corpus.WaitingCount(), corpus.Held());
        }

        // BEFORE routing, so it sees every request rather than one handler's.
        //
        // It lived inside the `/ingest` handler and two reviewers said the same thing: a header
        // arriving at `/health`, at an unknown path, or at any route added later would never be
        // noticed, and every new endpoint would have to remember to repeat the check. A
        // cross-cutting property belongs at the boundary it is a property of. (Code round,
        // gemini/codex.)
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

        // Unauthenticated, and it says nothing about the corpus: a health probe that reported a count
        // would be an unauthenticated read of how much anybody has contributed.
        app.MapGet("/health", () => Results.Ok(new Health("ok")));

        app.MapPost("/ingest", (UploadRequest? request, HttpRequest http) =>
            Accept(corpus, keywords, request, http, secret));

        await app.RunAsync();

        return 0;
    }

    /// <summary>
    /// One batch: authenticated once, then every item judged on its own.
    /// </summary>
    /// <remarks>
    /// <para><b>200 with a result per item, not a 4xx for the batch.</b> A whole-batch refusal is a
    /// batch the client retries unchanged for ever, with every valid pair stranded behind the invalid
    /// one. (Plan round, gemini.)</para>
    /// <para><b>No client address is read or logged.</b> `HttpRequest` is taken for its headers and
    /// nothing else, and no request-logging middleware is registered — but THIS PROCESS CANNOT KEEP
    /// THAT PROMISE ALONE. A reverse proxy writes `remote_addr` before the request reaches any route.
    /// The promise is a deployment obligation, written in the deploy notes with the nginx and Kestrel
    /// settings that keep it, and verified by reading the deployed stack's logs after a real ingest.
    /// Three reviewers said an in-process test cannot prove it, and they were right.</para>
    /// </remarks>
    private static Results<Ok<UploadAnswer>, UnauthorizedHttpResult, BadRequest<Problem>> Accept(
        Corpus corpus,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        UploadRequest? request,
        HttpRequest http,
        string secret)
    {
        var presented = Presented(http);
        var keyId = corpus.KeyFor(presented, secret);
        if (keyId.Length == 0)
        {
            // A revoked key and one that never existed answer the same thing, so this cannot be used
            // to discover which keys are real.
            return TypedResults.Unauthorized();
        }

        if (request?.Items is not { } items)
        {
            return TypedResults.BadRequest(new Problem("the request has no `items` list"));
        }

        if (items.Count > Ingest.MostPerBatch)
        {
            return TypedResults.BadRequest(
                new Problem($"a batch carries at most {Ingest.MostPerBatch} pairs"));
        }

        var answer = Ingest.Take(
            corpus, items, keywords, keyId, DateTime.UtcNow.ToString("O"));
        corpus.RecordSubmission(keyId);

        return TypedResults.Ok(answer);
    }

    /// <summary>The key a request presents, from the one header that carries it.</summary>
    /// <remarks>
    /// The scheme is matched case-insensitively, as RFC 6750 requires: a proxy that normalises the
    /// header to `bearer` is not an attacker, and a 401 for it is a morning somebody loses.
    /// </remarks>
    private static string Presented(HttpRequest http) =>
        http.Headers.Authorization.ToString() is { Length: > 7 } header
        && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..]
            : string.Empty;

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
    private static string Unknown(string[] args)
    {
        if (args.Length == 0 || !args[0].StartsWith("--", StringComparison.Ordinal))
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
    private static string Keywords()
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
}

/// <summary>What `/health` says, which is nothing about the corpus.</summary>
public sealed record Health(string Status);

/// <summary>Why a request was refused, in words rather than a code.</summary>
public sealed record Problem(string Why);

/// <summary>The serializer's whole world: nothing reflective, as every binary here is built.</summary>
[JsonSerializable(typeof(UploadRequest))]
[JsonSerializable(typeof(UploadAnswer))]
[JsonSerializable(typeof(Health))]
[JsonSerializable(typeof(Problem))]
internal sealed partial class BugsJson : JsonSerializerContext;
