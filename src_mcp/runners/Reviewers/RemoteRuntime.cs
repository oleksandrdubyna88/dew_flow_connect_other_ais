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
public sealed class RemoteRuntime(string id, string serverUrl) : IReviewerRuntime
{
    public string Provider => id;

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
                    "--vendor", id,
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
                    ((int)Math.Ceiling(settings.Timeout.TotalSeconds)).ToString(),
                ],
                worktreePath)
            {
                Timeout = settings.Timeout,
            },
            answerFile,
            this,
            // No SharedResource on purpose — see the remarks.
            string.Empty,
            settings.Model);
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
    /// Cancel a review whose shim was killed rather than allowed to finish.
    /// </summary>
    /// <returns>True when a job was found and the server was told; false when there was nothing to do.</returns>
    /// <remarks>
    /// The shim writes <c>&lt;server&gt; &lt;id&gt;</c> to its job file as soon as the server accepts,
    /// and deletes the file when it reaches a terminal state by itself. So a file that still exists
    /// names a review the server may still be running, and this is the only thing left that can stop
    /// it — the shim is already dead.
    /// </remarks>
    public static async Task<bool> CancelAbandonedAsync(
        string jobFile, string dataDir, HttpClient http, CancellationToken ct = default)
    {
        var (server, jobId) = ReadClaim(jobFile);
        if (jobId.Length == 0)
        {
            return false;
        }

        var token = TeamServerAuth.ReadToken(TeamServerAuth.TokenPath(dataDir, server));
        if (token.Length == 0)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete, TeamServerAuth.Endpoint(server, $"api/reviews/{jobId}"));
            request.Headers.Add("Authorization", "Bearer " + token);
            using var response = await http.SendAsync(request, ct);

            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // The server's own queue deadline is the backstop. This is the polite path, and a polite
            // path that throws would fail a round that had already produced its answer.
            return false;
        }
        finally
        {
            Forget(jobFile);
        }
    }

    /// <summary>The server and job id a shim claimed, or empty when there is no claim.</summary>
    public static (string Server, string JobId) ReadClaim(string jobFile)
    {
        try
        {
            if (!File.Exists(jobFile))
            {
                return (string.Empty, string.Empty);
            }

            var parts = File.ReadAllText(jobFile).Trim().Split(' ', 2);

            return parts.Length == 2 ? (parts[0], parts[1]) : (string.Empty, string.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (string.Empty, string.Empty);
        }
    }

    /// <summary>Record that this server is running this job, so a kill is still cancellable.</summary>
    public static void Claim(string jobFile, string serverUrl, string jobId)
    {
        try
        {
            File.WriteAllText(jobFile, $"{TeamServerAuth.Normalise(serverUrl)} {jobId}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing the claim costs a cancellation, not a review: the server's queue deadline still
            // ends the job. Failing the review over it would trade something small for something big.
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
