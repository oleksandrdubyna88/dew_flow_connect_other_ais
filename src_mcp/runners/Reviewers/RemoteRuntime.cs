using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A vendor whose reviews run on a Team server: one company subscription, shared.
/// </summary>
/// <remarks>
/// <para>Modelled on <see cref="LocalRuntime"/>, which already solves the same problem for a local
/// model — the adapter builds a command line that launches THIS binary in a shim mode, and the shim
/// does the HTTP. <see cref="LocalRuntime.SelfInvocation"/> answers the dotnet-host case and is
/// reused rather than copied.</para>
/// <para><b>No <c>SharedResource</c>.</b> The server has its own queue and its own per-account locks,
/// so a client-side per-resource semaphore would serialise reviews the server is perfectly able to
/// run at once. The global and per-provider caps still apply, because those are about how much this
/// machine is willing to have in flight.</para>
/// <para><b>The job id is written to a file, so the PARENT can cancel.</b> When a round is abandoned
/// the executor kills this child, and a killed process runs no cleanup — so a shim that meant to send
/// <c>DELETE</c> on its way out never does, and the review runs to completion on the team's
/// subscription for an answer nobody will collect. The shim writes the id the moment the server
/// accepts, and <see cref="CancelAbandoned"/> reads it and cancels. Raised as Blocking on the plan
/// round.</para>
/// </remarks>
public sealed class RemoteRuntime(string id, string serverUrl, string vendorOnServer = "") : IReviewerRuntime
{
    public string Provider => id;

    /// <summary>
    /// What this vendor is called ON THE SERVER — not what the row is called here.
    /// </summary>
    /// <remarks>
    /// A row is named <c>&lt;server&gt;-&lt;vendor&gt;</c> so that two Team servers each offering
    /// <c>codex</c> do not collide on one id, since the id names the row, its usage history and its
    /// vault key. The server knows only <c>codex</c>. Sending the row id would be refused by every
    /// server, and the probe would report a vendor the server "does not offer" — which reads exactly
    /// like a typo. Empty falls back to the row id, which is what every hand-written row does.
    /// </remarks>
    public string VendorOnServer => vendorOnServer.Length > 0 ? vendorOnServer : id;

    public string DefaultExecutable => LocalRuntime.SelfInvocation().Executable;

    /// <summary>The server this vendor's reviews go to, in its one canonical spelling.</summary>
    public string ServerUrl => TeamServerAuth.Normalise(serverUrl);

    public ReviewerInvocation Build(
        ReviewRole role,
        string prompt,
        string worktreePath,
        string schemaFilePath,
        string outputDir,
        ReviewerSettings settings)
    {
        Directory.CreateDirectory(outputDir);
        var stem = $"remote-{role}-{Guid.NewGuid():N}";
        var promptFile = Path.Combine(outputDir, stem + ".prompt");
        var answerFile = Path.Combine(outputDir, stem + ".json");
        var jobFile = Path.Combine(outputDir, stem + ".job");
        File.WriteAllText(promptFile, prompt);

        var (self, prefix) = LocalRuntime.SelfInvocation();
        var executable = settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : self;
        // A path somebody set is taken as the whole answer: they named an executable, not a host to
        // pass a dll to.
        var leading = settings.ExecutablePath.Length > 0 ? Array.Empty<string>() : prefix.ToArray();

        return new ReviewerInvocation(
            id,
            role,
            new ProcessRequest(executable,
                [
                    .. leading,
                    "--ask-remote",
                    "--server", ServerUrl,
                    "--vendor", VendorOnServer,
                    "--model", settings.Model,
                    "--role", role.ToString(),
                    "--prompt-file", promptFile,
                    "--schema-file", schemaFilePath,
                    "--out", answerFile,
                    // A PATH, never the token. It is not in argv, not in settings.json, not in a log
                    // line — and the file itself is owner-only, which is the half that protects it
                    // from the next user of a shared machine.
                    "--token-file", TeamServerAuth.TokenPath(settings.DataDir, ServerUrl),
                    "--job-file", jobFile,
                    // The shim's own deadline, derived from the one the executor will enforce so the
                    // two cannot disagree. Deliberately the shorter: reaching it cancels the job and
                    // exits with a reason, while being killed leaves the round guessing.
                    "--timeout-seconds",
                    LocalAsk.ShimDeadlineSeconds(settings.Timeout).ToString(),
                    // How long the VENDOR may take, which is a different clock from the one above —
                    // that one is how long this client waits before it cancels and reports.
                    "--vendor-timeout-seconds",
                    VendorBudgetSeconds(settings.Timeout).ToString(),
                ],
                worktreePath)
            {
                Timeout = settings.Timeout,
            },
            answerFile,
            this,
            // No SharedResource on purpose — see the remarks.
            string.Empty,
            settings.Model,
            // The parent reads this after killing the child. It is the only thing left that can stop
            // a review still running on the team's subscription.
            jobFile);
    }

    /// <summary>The usage the shim printed, as <see cref="LocalRuntime.ReadUsage"/> reads its own.</summary>
    public Usage ReadUsage(ReviewerInvocation invocation, ProcessResult result)
    {
        try
        {
            using var parsed = JsonDocument.Parse(result.StdOut);
            var root = parsed.RootElement;

            return new Usage(
                root.TryGetProperty("tokensIn", out var input) && input.TryGetInt64(out var tin) ? tin : 0,
                root.TryGetProperty("tokensOut", out var output) && output.TryGetInt64(out var tout) ? tout : 0,
                null);
        }
        catch (JsonException)
        {
            return new Usage(0, 0, null);
        }
    }

    /// <summary>
    /// The vendor's budget, as the SERVER will accept it.
    /// </summary>
    /// <remarks>
    /// `POST /api/reviews` refuses anything outside 30..1800 seconds, so a person who set an
    /// eight-second reviewer timeout would have had every remote review answered `400` before it
    /// started — a configuration mistake rendered as a server error. Clamping is right rather than
    /// refusing: the shim's OWN deadline still honours the shorter setting, so the review is still
    /// abandoned when the person said, and the only thing the clamp changes is what the server is
    /// told about the vendor. Raised by two reviewers on the code round.
    /// </remarks>
    public static int VendorBudgetSeconds(TimeSpan timeout) =>
        Math.Clamp((int)Math.Ceiling(timeout.TotalSeconds), MinVendorSeconds, MaxVendorSeconds);

    /// <summary>The server's own range, restated here because the two binaries ship separately.</summary>
    public const int MinVendorSeconds = 30;

    /// <inheritdoc cref="MinVendorSeconds"/>
    public const int MaxVendorSeconds = 1800;

    /// <summary>One client for the courtesy cancellations; they are rare and short.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Stop a review whose shim we killed — the executor's abandon hook.
    /// </summary>
    /// <remarks>
    /// This is the half that made the job file worth writing. Without it the mechanism existed and
    /// nothing called it, which a reviewer caught on the code round: `Claim` wrote a file that only a
    /// test ever read.
    /// </remarks>
    public async Task AbandonAsync(ReviewerInvocation invocation, CancellationToken ct = default)
    {
        if (invocation.JobFile.Length == 0)
        {
            return;
        }

        await CancelAbandonedAsync(invocation.JobFile, Http, ct);
    }

    /// <summary>
    /// Cancel a review whose shim was killed rather than allowed to finish.
    /// </summary>
    /// <returns>True when the server was told; false when there was nothing to do, or it could not be told.</returns>
    /// <remarks>
    /// <para>The shim writes <c>&lt;server&gt; &lt;id&gt;</c> to its job file as soon as the server
    /// accepts, and deletes the file when it reaches a terminal state by itself. So a file that still
    /// exists names a review the server may still be running, and this is the only thing left that can
    /// stop it — the shim is already dead.</para>
    /// <para><b>The claim is kept when the cancellation FAILED.</b> It used to be deleted in a
    /// `finally`, so a client that was briefly offline at exactly the wrong moment lost the job id for
    /// ever and the review ran to completion anyway — the one outcome the whole mechanism exists to
    /// prevent. It is forgotten on success, and on a `404`, which means the server has no such job and
    /// never will. Raised on the code round.</para>
    /// </remarks>
    public static async Task<bool> CancelAbandonedAsync(
        string jobFile, HttpClient http, CancellationToken ct = default)
    {
        var claim = ReadClaim(jobFile);
        var token = TeamServerAuth.ReadToken(claim.TokenFile);
        if (claim.JobId.Length == 0 || token.Length == 0)
        {
            // No claim, or signed out since the review started. The claim STAYS in the second case:
            // signing in again is what makes this cancellable, and deleting it now would throw away
            // the only way to do so.
            return false;
        }

        var status = await DeleteAsync(claim, token, http, ct);
        // Forgotten on success, and on a 404 — which means the server has no such job and never will.
        if (status is System.Net.HttpStatusCode.NotFound || Succeeded(status))
        {
            Forget(jobFile);
        }

        return Succeeded(status);
    }

    private static bool Succeeded(System.Net.HttpStatusCode? status) =>
        status is { } answered && (int)answered is >= 200 and < 300;

    /// <summary>The DELETE itself, or null when the server could not be reached.</summary>
    /// <remarks>
    /// Null rather than an exception: the claim is then KEPT, because a client that was briefly
    /// offline must still be able to try again. A polite path that threw would fail a round that had
    /// already produced its answer.
    /// </remarks>
    private static async Task<System.Net.HttpStatusCode?> DeleteAsync(
        RemoteClaim claim, string token, HttpClient http, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete, TeamServerAuth.Endpoint(claim.Server, $"api/reviews/{claim.JobId}"));
            request.Headers.Add("Authorization", "Bearer " + token);
            // The server judges this BEFORE the token, and answers 426 without it — so a cancellation
            // that omitted it was refused by every server it was sent to. Caught on the code round.
            request.Headers.Add(RemoteAsk.ContractHeader, RemoteAsk.ContractVersion.ToString());
            using var response = await http.SendAsync(request, ct);

            return response.StatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// One outstanding review: everything needed to stop it, and nothing else.
    /// </summary>
    /// <remarks>
    /// It carries the TOKEN FILE rather than a data directory because the reader is the parent
    /// process, which resolves the adapter through <c>RuntimeResolution.For</c> — from a vendor row,
    /// which has no data directory in it. A claim that describes how to authenticate against itself
    /// needs nothing from whoever finds it.
    /// </remarks>
    public sealed record RemoteClaim(string Server, string JobId, string TokenFile)
    {
        public static readonly RemoteClaim None = new(string.Empty, string.Empty, string.Empty);
    }

    /// <summary>The claim a shim left, or <see cref="RemoteClaim.None"/> when there is none.</summary>
    public static RemoteClaim ReadClaim(string jobFile)
    {
        try
        {
            if (!File.Exists(jobFile))
            {
                return RemoteClaim.None;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(jobFile), RemoteClaimContext.Default.RemoteClaim)
                ?? RemoteClaim.None;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return RemoteClaim.None;
        }
    }

    /// <summary>
    /// Record that this server is running this job, so a kill is still cancellable.
    /// </summary>
    /// <returns>Empty when the claim was written; otherwise why it could not be.</returns>
    /// <remarks>
    /// The failure is RETURNED rather than swallowed. Losing the claim costs a cancellation, not a
    /// review — the server's queue deadline still ends the job eventually — so this must not fail the
    /// review; but a silent loss means a person is later billed for work nobody can explain, with
    /// nothing anywhere saying why. The shim prints it. Raised twice on the code round.
    /// </remarks>
    public static string Claim(string jobFile, string serverUrl, string jobId, string tokenFile)
    {
        try
        {
            File.WriteAllText(
                jobFile,
                JsonSerializer.Serialize(
                    new RemoteClaim(TeamServerAuth.Normalise(serverUrl), jobId, tokenFile),
                    RemoteClaimContext.Default.RemoteClaim));

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>The job reached a terminal state by itself; there is nothing to cancel.</summary>
    public static void Forget(string jobFile)
    {
        try
        {
            File.Delete(jobFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A stale claim costs one DELETE for a job that is already finished, which the server
            // answers 404 to and nobody notices.
        }
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(RemoteRuntime.RemoteClaim))]
internal sealed partial class RemoteClaimContext : System.Text.Json.Serialization.JsonSerializerContext;
