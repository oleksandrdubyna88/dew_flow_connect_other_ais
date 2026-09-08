using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>What a poll of a Team server review came back as.</summary>
public enum RemoteState
{
    Queued,
    Running,
    Done,
    Failed,

    /// <summary>
    /// A status this client has no branch for.
    /// </summary>
    /// <remarks>
    /// It is NOT folded into <see cref="Queued"/>, which is what it used to be. A newer server adding
    /// a terminal status would then have been read as "still waiting", and the shim would have polled
    /// a finished review until its own deadline and reported it as too slow — a wrong sentence about
    /// the wrong thing. Raised on the code round.
    /// </remarks>
    Unknown,
}

/// <summary>One poll's answer, parsed.</summary>
/// <param name="Position">Where it sits in its vendor's queue; 0 once it is running.</param>
/// <param name="Answer">The vendor's RAW text. This shim does not parse it — the round does.</param>
public sealed record RemotePoll(
    RemoteState State,
    string RawStatus,
    int Position,
    string Answer,
    string Failure,
    string Reason,
    long TokensIn,
    long TokensOut);

/// <summary>
/// The pure half of <c>--ask-remote</c>: what to send, how to read what comes back, and every
/// sentence a person will see.
/// </summary>
/// <remarks>
/// The twin of <see cref="LocalAsk"/>, and pure for the same reason: every exit path can be tested
/// without a server, so the wording is checked rather than hoped for. A sentence that blames the
/// wrong thing sends somebody to fix something that was never broken.
/// </remarks>
public static class RemoteAsk
{
    /// <summary>Exit codes. The same vocabulary <c>--ask-local</c> uses.</summary>
    public const int Ok = 0;
    public const int BadUsage = 64;
    public const int MissingSchema = 65;
    public const int Unreachable = 69;
    public const int VendorFailed = 70;
    public const int TooOld = 76;
    public const int NotSignedIn = 77;

    /// <summary>
    /// The review did not finish in time, and has been cancelled.
    /// </summary>
    /// <remarks>
    /// Its own code rather than <see cref="Unreachable"/>, which is what it used to return. The
    /// sentence said "did not finish within 90s" while the exit code said the server could not be
    /// reached — so anybody reading the code rather than the text went to check their network for a
    /// problem whose cure is a longer timeout or more accounts. Raised on the code round.
    /// </remarks>
    public const int TooSlow = 78;

    /// <summary>How long to leave between polls while a review is queued or running.</summary>
    /// <remarks>
    /// The server long-polls up to 25 seconds and returns the moment the state changes, so this is
    /// how long to wait after a poll that came back unchanged — not how often the server is asked.
    /// One reviewer therefore makes about two requests a minute, which is why polling never reaches
    /// the server's rate limiter.
    /// </remarks>
    public static readonly TimeSpan PollGap = TimeSpan.FromSeconds(1);

    /// <summary>The header every request carries, and what this client speaks.</summary>
    /// <remarks>
    /// The server judges this BEFORE it looks at the token, so a client too old to be served is told
    /// to update rather than handed a 401 about a token that was never the problem. Declared here
    /// rather than imported from the server: these are two binaries that ship separately, and a
    /// shared constant would mean the client could only be as old as the server it was built beside
    /// — which is the exact thing the header exists to make unnecessary.
    /// </remarks>
    public const string ContractHeader = "X-Coai-Contract";

    /// <inheritdoc cref="ContractHeader"/>
    public const int ContractVersion = 1;

    /// <summary>How long each long poll asks the server to hold the connection.</summary>
    public const int LongPollSeconds = 25;

    /// <summary>The body of <c>POST /api/reviews</c>.</summary>
    public static string RequestBody(string vendor, string model, string role, string prompt, int timeoutSeconds) =>
        JsonSerializer.Serialize(
            new RemoteRequest(vendor, model, prompt, role, timeoutSeconds),
            RemoteJsonContext.Default.RemoteRequest);

    /// <summary>The id in a 202, or empty when the answer was not one.</summary>
    public static string AcceptedId(string body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, RemoteJsonContext.Default.RemoteAccepted)?.Id ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>One poll's answer, or null when the body was not one.</summary>
    public static RemotePoll? ReadPoll(string body)
    {
        try
        {
            var status = JsonSerializer.Deserialize(body, RemoteJsonContext.Default.RemoteStatus);

            return status is null
                ? null
                : new RemotePoll(
                    StateOf(status.Status),
                    status.Status ?? string.Empty,
                    status.Position,
                    status.Answer ?? string.Empty,
                    status.Failure ?? string.Empty,
                    status.Reason ?? string.Empty,
                    status.TokensIn,
                    status.TokensOut);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The usage line the runtime reads back off stdout.</summary>
    public static string UsageLine(long tokensIn, long tokensOut) =>
        JsonSerializer.Serialize(new RemoteUsage(tokensIn, tokensOut, null), RemoteJsonContext.Default.RemoteUsage);

    /// <summary>Not signed in at all — there is no token file for this server.</summary>
    public static string NotSignedInMessage(string serverUrl) =>
        $"not signed in to the Team server at {serverUrl} — sign in from the panel's Team servers section";

    /// <summary>Signed in once, but the server will not take this token any more.</summary>
    public static string RejectedMessage(string serverUrl) =>
        $"the Team server at {serverUrl} rejected this token — sign in again from the panel's Team servers section";

    /// <summary>Authenticated, and not welcome.</summary>
    /// <remarks>
    /// A different sentence from the one above on purpose: signing in again cannot fix being outside
    /// the company's domain, and telling somebody to try would waste their afternoon.
    /// </remarks>
    public static string ForbiddenMessage(string serverUrl) =>
        $"the Team server at {serverUrl} refused this account — it is outside the allowed company domain";

    public static string TooOldMessage(string serverUrl, string serverSaid) =>
        $"coai-mcp is older than the Team server at {serverUrl} requires — update it from the panel's "
        + $"Server section. The server said: {Trim(serverSaid, 200)}";

    public static string UnreachableMessage(string serverUrl, string detail) =>
        $"the Team server at {serverUrl} could not be reached: {Trim(detail, 200)}";

    /// <summary>
    /// The server answered something this shim has no branch for.
    /// </summary>
    /// <remarks>
    /// A 500, a 404 from a proxy that swallowed the route, an HTML error page from something in
    /// front of it. Left unhandled these become a crash or a hang; named, they become one sentence
    /// with the status in it, which is the difference between "the server is broken" and "something
    /// in the middle is". (codex, plan round.)
    /// </remarks>
    public static string UnexpectedMessage(string serverUrl, int status, string body) =>
        $"the Team server at {serverUrl} answered {status}, which this client has no handling for. "
        + $"It said: {Trim(body, 200)}";

    /// <summary>A body that should have been JSON and was not.</summary>
    public static string UnreadableMessage(string serverUrl, string body) =>
        $"the Team server at {serverUrl} answered something this client could not read as a review. "
        + $"It said: {Trim(body, 200)}";

    /// <summary>The shim gave up. The job is cancelled before this is printed.</summary>
    /// <remarks>
    /// It names the position AND what happens next, because "position 50" alone tells somebody
    /// nothing they can act on — they cannot tell five minutes from five hours, and the usual
    /// response to not knowing is to retry, which lengthens the queue they are complaining about.
    /// (local, plan round.)
    /// </remarks>
    public static string TooSlowMessage(string serverUrl, TimeSpan waited, int position) =>
        $"the review did not finish within {waited.TotalSeconds:0}s on the Team server at {serverUrl}"
        + (position > 0 ? $" (it was {position} in the queue)" : " (it had started)")
        + ". It has been cancelled, so nothing is still running. Raise COAI_REVIEWER_TIMEOUT_MINUTES, "
        + "or ask the operator for more accounts on that vendor.";

    /// <summary>A status from a server this client is too old to understand.</summary>
    public static string UnknownStateMessage(string serverUrl, string status) =>
        $"the Team server at {serverUrl} reported a review status this client does not know "
        + $"('{Trim(status, 60)}') — update coai-mcp from the panel's Server section";

    /// <summary>The vendor itself failed, in its own words.</summary>
    public static string FailedMessage(string vendor, string failure, string reason) =>
        $"the Team server's {vendor} reviewer failed ({(failure.Length > 0 ? failure : "no reason given")})"
        + (reason.Length > 0 ? $": {Trim(reason, 400)}" : string.Empty);

    /// <summary>Still waiting for a free account, and how many are ahead.</summary>
    public static string QueuedMessage(string serverUrl, int position) =>
        $"queued on the Team server at {serverUrl}, {position} ahead of it";

    /// <summary>On an account now, running.</summary>
    public static string RunningMessage(string serverUrl) =>
        $"running on the Team server at {serverUrl}";

    /// <summary>
    /// Whether a line the shim wrote is PROGRESS rather than a verdict.
    /// </summary>
    /// <remarks>
    /// <para>The shim writes both to one stderr, and whoever reads that stream to explain a failure
    /// has to be able to tell them apart. Measured on a real gate, 2026-09-07: a Team-server
    /// reviewer failed and the round reported
    /// <c>exit 70: [coai-mcp] claude: running on the Team server at …</c> — a reviewer described as
    /// RUNNING in the sentence announcing that it had stopped, because the reason-picker took the
    /// first line that was not obviously noise and a progress note is the first line there is.</para>
    /// <para><b>Asking "is this progress" rather than "does this announce a failure" is the whole
    /// point.</b> The first attempt taught the picker the word <c>failed</c>, and three reviewers
    /// independently broke it: <c>0 failed, 3 passed</c> and <c>3 tests failed</c> are tallies that
    /// announce nothing, while <c>Step 3 failed</c> and <c>Job 12 failed</c> are real verdicts with
    /// a digit in front — no rule over that word can have it both ways. These two sentences, by
    /// contrast, are ours: we author them, there are exactly two, and neither can be confused with
    /// anything a vendor CLI prints.</para>
    /// <para>Matched on the STEM rather than the whole sentence, because the server URL and the
    /// queue position are interpolated into them.</para>
    /// </remarks>
    public static bool IsProgress(string line) =>
        line.Contains("queued on the Team server", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("running on the Team server", StringComparison.OrdinalIgnoreCase);

    /// <summary>Which exit an HTTP status means, for a status this shim knows.</summary>
    /// <remarks>
    /// Returns null when the status is not one of the handled ones, so the caller can produce the
    /// "no handling for" sentence rather than guessing at an exit code.
    /// </remarks>
    public static int? ExitForStatus(int status) => status switch
    {
        401 => NotSignedIn,
        403 => NotSignedIn,
        426 => TooOld,
        >= 200 and < 300 => Ok,
        _ => null,
    };

    private static RemoteState StateOf(string? status) => status?.ToLowerInvariant() switch
    {
        "done" => RemoteState.Done,
        "failed" => RemoteState.Failed,
        "running" => RemoteState.Running,
        "queued" => RemoteState.Queued,
        _ => RemoteState.Unknown,
    };

    private static string Trim(string text, int max)
    {
        var single = (text ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();

        return single.Length <= max ? single : single[..max] + "…";
    }
}

internal sealed record RemoteRequest(string Vendor, string Model, string Prompt, string Role, int TimeoutSeconds);

internal sealed record RemoteAccepted(string? Id, int Position);

internal sealed record RemoteStatus(
    string? Id,
    string? Status,
    int Position,
    string? Answer,
    double Seconds,
    long TokensIn,
    long TokensOut,
    string? Failure,
    string? Reason);

internal sealed record RemoteUsage(long TokensIn, long TokensOut, double? CostUsd);

internal sealed record RemoteError(string? Error);

/// <summary>Every shape this shim puts on or takes off the wire, source-generated.</summary>
/// <remarks>
/// The server serialises camelCase and reflection is off in the AOT build of both binaries, so the
/// naming policy is declared here rather than left to a default that differs between them.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(RemoteRequest))]
[JsonSerializable(typeof(RemoteAccepted))]
[JsonSerializable(typeof(RemoteStatus))]
[JsonSerializable(typeof(RemoteUsage))]
[JsonSerializable(typeof(RemoteError))]
internal sealed partial class RemoteJsonContext : JsonSerializerContext;
