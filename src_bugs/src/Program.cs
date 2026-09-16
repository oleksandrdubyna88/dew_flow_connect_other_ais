using System.Text.Json.Serialization;
using CoaiBugs;
using CoaiMcp.Core.Collecting;
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
        if (args.Length > 0 && args[0] is "--issue-key" or "--revoke" or "--promote" or "--waiting")
        {
            return Admin.Run(args, Secret(), Data());
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
        builder.Services.ConfigureHttpJsonOptions(
            options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, BugsJson.Default));
        builder.WebHost.ConfigureKestrel(
            kestrel => kestrel.Limits.MaxRequestBodySize = Ingest.MostBytes);

        var app = builder.Build();
        using var corpus = Corpus.Open(Path.Combine(Data(), "coai-bugs.db"));
        var keywords = SkeletonKeywords.Read(Root());

        // Unauthenticated, and it says nothing about the corpus: a health probe that reported a count
        // would be an unauthenticated read of how much anybody has contributed.
        app.MapGet("/health", () => Results.Ok(new Health("ok")));

        app.MapPost("/ingest", (IngestRequest? request, HttpRequest http) =>
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
    /// nothing else, and no logging middleware is registered — but THIS PROCESS CANNOT KEEP THAT
    /// PROMISE ALONE. A reverse proxy writes `remote_addr` before the request reaches any route. The
    /// promise is a deployment obligation, written in the deploy notes with the nginx and Kestrel
    /// settings that keep it, and verified by reading the deployed stack's logs after a real ingest.
    /// Three reviewers said an in-process test cannot prove it, and they were right.</para>
    /// </remarks>
    private static Results<Ok<IngestAnswer>, UnauthorizedHttpResult, BadRequest<Problem>> Accept(
        Corpus corpus,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        IngestRequest? request,
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
        corpus.Counted(keyId);

        return TypedResults.Ok(answer);
    }

    /// <summary>The key a request presents, from the one header that carries it.</summary>
    private static string Presented(HttpRequest http) =>
        http.Headers.Authorization.ToString() is { Length: > 7 } header
        && header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? header[7..]
            : string.Empty;

    private static string Secret() =>
        Environment.GetEnvironmentVariable(SecretVariable) ?? string.Empty;

    private static string Data() =>
        Environment.GetEnvironmentVariable("COAI_BUGS_DATA") ?? Directory.GetCurrentDirectory();

    /// <summary>Where `shared/skeleton-keywords.txt` is, walking up from the binary.</summary>
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "shared", SkeletonKeywords.FileName)))
            {
                return dir.FullName;
            }
        }

        throw new FileNotFoundException(
            $"shared/{SkeletonKeywords.FileName} was not found beside this binary; it is the word "
            + "list the alphabet check is made of and the server cannot validate without it");
    }
}

/// <summary>What `/health` says, which is nothing about the corpus.</summary>
public sealed record Health(string Status);

/// <summary>Why a request was refused, in words rather than a code.</summary>
public sealed record Problem(string Why);

/// <summary>The serializer's whole world: nothing reflective, as every binary here is built.</summary>
[JsonSerializable(typeof(IngestRequest))]
[JsonSerializable(typeof(IngestAnswer))]
[JsonSerializable(typeof(Health))]
[JsonSerializable(typeof(Problem))]
internal sealed partial class BugsJson : JsonSerializerContext;
