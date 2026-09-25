using System.Globalization;
using System.Text.Json;
using CoaiMcp.Core.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A hosted OpenAI-compatible API — xAI, DashScope, any <c>/v1</c> — asked directly, with a key from
/// the vault.
/// </summary>
/// <remarks>
/// <para><b>The seventh adapter, and the second whose "CLI" is this binary.</b> Like
/// <see cref="LocalRuntime"/> it self-invokes — <c>coai-mcp --ask-api</c> — so the executor, the
/// scheduler, the usage parser and the failure classification see a process like every other
/// reviewer. Unlike the local runtime it declares NO shared resource: a vendor's fleet is bounded by
/// its rate limit, not by a card on this machine, so an api reviewer takes the machine lane and its
/// per-provider cap and never waits on <see cref="EngineLease"/> or the GPU stand-down
/// (PLAN_feature_review.md §4.10).</para>
/// <para><b>The key travels in the child's ENVIRONMENT and nowhere else.</b> Never in argv — a
/// command line is visible to every process listing on the machine and ends up in logs — and never in
/// the prompt file. <c>ApiRuntimeTests</c> asserts no argv element equals the key.</para>
/// <para><b>Not the custom-codex runtime</b>, for the reason the local one is not: codex's own system
/// prompt is 21k tokens before any review content, and it sends fields a reasoning endpoint refuses.
/// A direct call sends exactly what the dialect says.</para>
/// </remarks>
public sealed class ApiRuntime(string id, string baseUrl) : IReviewerRuntime
{
    /// <summary>The environment variable the shim reads its bearer key from. The ONE way a key reaches it.</summary>
    public const string KeyVariable = "COAI_API_KEY";

    public string Provider => id;

    /// <summary>This binary, however it was started — see <see cref="LocalRuntime.SelfInvocation"/>.</summary>
    public string DefaultExecutable => LocalRuntime.SelfInvocation().Executable;

    /// <summary>The dialect a row that named none speaks.</summary>
    public static string DialectOf(ReviewerSettings settings) =>
        settings.Dialect.Trim().Length > 0 ? settings.Dialect.Trim().ToLowerInvariant() : ApiDialects.OpenAiName;

    public ReviewerInvocation Build(
        string role,
        string prompt,
        string worktreePath,
        string schemaFilePath,
        string outputDir,
        ReviewerSettings settings)
    {
        Directory.CreateDirectory(outputDir);
        var promptFile = Path.Combine(outputDir, $"api-{FileName.Safe(role)}-{Guid.NewGuid():N}.prompt");
        var answerFile = Path.Combine(outputDir, $"api-{FileName.Safe(role)}-{Guid.NewGuid():N}.json");
        File.WriteAllText(promptFile, prompt);

        var (self, prefix) = LocalRuntime.SelfInvocation();
        var executable = settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : self;
        var leading = settings.ExecutablePath.Length > 0 ? Array.Empty<string>() : prefix.ToArray();

        return new ReviewerInvocation(
            id,
            role,
            new ProcessRequest(executable,
                [
                    ..leading,
                    "--ask-api",
                    "--vendor", id,
                    // Empty stays empty: the shim refuses it with 65 and a sentence, which is a better
                    // report than a request sent to nowhere.
                    "--endpoint", baseUrl.Length > 0 ? LocalRuntime.OpenAiBaseOf(baseUrl) : string.Empty,
                    "--model", settings.Model,
                    "--dialect", DialectOf(settings),
                    "--prompt-file", promptFile,
                    "--schema-file", schemaFilePath,
                    "--out", answerFile,
                    "--timeout-seconds",
                    LocalAsk.ShimDeadlineSeconds(settings.Timeout).ToString(CultureInfo.InvariantCulture),
                    "--max-tokens",
                    settings.MaxTokens.ToString(CultureInfo.InvariantCulture),
                    ..(settings.ReasoningEffort.Trim().Length > 0
                        ? new[] { "--reasoning-effort", settings.ReasoningEffort.Trim() }
                        : []),
                ],
                worktreePath)
            {
                // The key, and ONLY in the environment. An empty key sends nothing, and the shim
                // then refuses with 65 naming the vault entry rather than sending an unauthenticated
                // request that the vendor would answer 401 to after a network round trip.
                Environment = settings.ApiKey.Length > 0
                    ? new Dictionary<string, string?> { [KeyVariable] = settings.ApiKey }
                    : new Dictionary<string, string?>(),
                Timeout = settings.Timeout,
            },
            answerFile,
            this,
            // No shared resource: a hosted vendor's fleet is somebody else's and bounded by a rate
            // limit. This is what keeps an api reviewer off the engine lane and out of EngineLease.
            SharedResource: string.Empty,
            Model: settings.Model,
            Effort: settings.ReasoningEffort.Trim());
    }

    /// <summary>What the run consumed, from the shim's stdout line — the same shape the local shim prints.</summary>
    /// <remarks>
    /// Money is null: the endpoint reports tokens and this build ships no price table. The panel's
    /// per-vendor rates price them, marked with a tilde, exactly as codex's tokens are.
    /// </remarks>
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
}
