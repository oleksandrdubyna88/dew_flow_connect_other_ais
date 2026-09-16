using System.Reflection;
using System.Text.Json.Serialization;
using CoaiBugs;
using CoaiMcp.Core.Collecting;
using CoaiMcp.ServiceDefaults;
using Serilog;
using Microsoft.AspNetCore.Http.HttpResults;

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
// tests host this server in-process. `coai-server` is arranged the same way.
internal sealed class Program
{
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

        var app = builder.Build();
        using var corpus = Corpus.Open(Path.Combine(Data(), "coai-bugs.db"));
        var keywords = SkeletonKeywords.From(Keywords());
        app.Logger.LogInformation(
            "coai-bugs is up: {Languages} keyword lists, {Waiting} waiting, {Held} in the corpus",
            keywords.Count, corpus.WaitingCount(), corpus.Held());

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
