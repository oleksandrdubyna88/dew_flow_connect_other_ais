using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A model served on THIS machine, asked directly over its OpenAI-compatible endpoint.
/// </summary>
/// <remarks>
/// <para><b>Not the custom-endpoint runtime, and the difference was measured.</b>
/// <see cref="CustomCodexRuntime"/> points the Codex CLI at an OpenAI-compatible base, and that
/// does reach a local Ollama and answer. But codex's own system prompt is 21k tokens before any
/// review content: a model with an 8k window is refused outright, and a larger one pays for a
/// prompt with nothing to do with the review. A direct call pays none of it.</para>
///
/// <para><b>Why it is still a process.</b> <see cref="IReviewerRuntime.Build"/> returns a
/// <see cref="ProcessRequest"/> and the executor runs it; letting an adapter answer in-process
/// instead would reach the scheduler, the concurrency accounting, the usage parser and the failure
/// classification. So the "CLI" is this binary in <c>--ask-local</c> mode. That is not a
/// consolation prize — a hung local generation is ordinary rather than exceptional, and the process
/// boundary buys a hard timeout for nothing.</para>
///
/// <para><b>What killing it actually stops, measured rather than assumed.</b> A long generation was
/// started through this shim, the process killed with force, and GPU compute was 0% six seconds
/// later. The mechanism is the SOCKET closing when the process dies, which Ollama reads as a client
/// disconnect and cancels on — not process-tree termination, which an earlier version of this
/// comment claimed and which would not have stopped a daemon outside the tree. Verified for Ollama;
/// not measured for vLLM, where a non-streaming request may not notice the drop.</para>
///
/// <para><b>The prompt travels as a FILE.</b> A review prompt is thousands of characters of diff and
/// schema containing quotes, newlines and backticks. Every shell-quoting failure this project has
/// had came from text on a command line.</para>
/// </remarks>
public sealed class LocalRuntime(string id, string baseUrl) : IReviewerRuntime
{
    /// <summary>Ollama's own default, and the only port it is on unless somebody moved it.</summary>
    public const string DefaultEndpoint = "http://127.0.0.1:11434/v1";

    public string Provider => id;

    /// <summary>
    /// This binary, however it was started. There is no vendor CLI to find, install or update.
    /// </summary>
    /// <remarks>
    /// <para><b>Not simply <c>Environment.ProcessPath</c>.</b> That is the app in a Native AOT
    /// release — which is what ships — and it is <c>dotnet.exe</c> when the same code runs
    /// framework-dependent as <c>dotnet coai-mcp.dll</c>, which is how the debug build and the test
    /// runner start it. Launching <c>dotnet --ask-local</c> dies on an unrecognised option from the
    /// CLI driver, and the failure would look like the local engine's fault.</para>
    /// <para>Raised as Blocking by this product's own gate reviewing this plan. The test that was
    /// supposed to cover it asserted the executable is not codex, agy or claude — and
    /// <c>dotnet</c> is none of those, so a test that only ruled things out could not notice the
    /// wrong right answer.</para>
    /// </remarks>
    public string DefaultExecutable => SelfInvocation().Executable;

    /// <summary>How to start this binary again: the executable, and the arguments that must precede
    /// ours.</summary>
    internal static (string Executable, IReadOnlyList<string> Prefix) SelfInvocation()
    {
        var process = Environment.ProcessPath ?? "coai-mcp";
        var name = Path.GetFileNameWithoutExtension(process);
        if (!string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (process, []);
        }

        // Framework-dependent: the host is the process, and the app is a dll beside it.
        var dll = Path.Combine(AppContext.BaseDirectory, "coai-mcp.dll");

        return (process, [dll]);
    }

    public ReviewerInvocation Build(
        ReviewRole role,
        string prompt,
        string worktreePath,
        string schemaFilePath,
        string outputDir,
        ReviewerSettings settings)
    {
        Directory.CreateDirectory(outputDir);
        var promptFile = Path.Combine(outputDir, $"local-{role}-{Guid.NewGuid():N}.prompt");
        var answerFile = Path.Combine(outputDir, $"local-{role}-{Guid.NewGuid():N}.json");
        File.WriteAllText(promptFile, prompt);

        var endpoint = baseUrl.Length > 0 ? baseUrl : DefaultEndpoint;
        var (self, prefix) = SelfInvocation();
        var executable = settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : self;
        // A path somebody set is taken as the whole answer: they named an executable, not a host to
        // pass a dll to.
        var leading = settings.ExecutablePath.Length > 0 ? Array.Empty<string>() : prefix.ToArray();

        return new ReviewerInvocation(
            id,
            role,
            new ProcessRequest(executable,
                [
                    ..leading,
                    "--ask-local",
                    "--endpoint", endpoint,
                    "--model", settings.Model,
                    "--prompt-file", promptFile,
                    "--schema-file", schemaFilePath,
                    "--out", answerFile,
                    // The shim's own deadline, derived from the one the executor will enforce so the
                    // two cannot disagree. It is deliberately the shorter of the pair: reaching it
                    // exits with a reason, while being killed leaves the round guessing.
                    "--timeout-seconds",
                    LocalAsk.ShimDeadlineSeconds(settings.Timeout).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ],
                worktreePath)
            {
                Timeout = settings.Timeout,
            },
            answerFile,
            this);
    }

    /// <summary>
    /// What the run consumed, from the ENDPOINT's own report.
    /// </summary>
    /// <remarks>
    /// Measured against the real thing: Ollama's <c>/v1/chat/completions</c> answers with
    /// <c>usage: {prompt_tokens, completion_tokens, total_tokens}</c>, so a local round is counted
    /// like any other rather than showing a dash. The shim prints those two numbers on stdout in the
    /// shape this method reads.
    ///
    /// <para><b>Money is never reported and that is not an omission.</b> A model on your own hardware
    /// costs electricity and a busy card, neither of which this product can see. Reporting 0 would
    /// read as free, and free and unpriced are different facts — the same rule the spending chart
    /// already applies to codex and antigravity.</para>
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
