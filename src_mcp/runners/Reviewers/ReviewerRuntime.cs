using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>The three code roles and the plan critique. The third is code-only on purpose —
/// called "UI", a model tries to picture the page it cannot see.</summary>
public enum ReviewRole
{
    PlanCritique,
    Architecture,
    SecurityReliability,
    UxDxPerformance,
}

/// <summary>How one provider is configured. The key travels in env, never argv.</summary>
public sealed record ReviewerSettings(string Provider)
{
    /// <summary>The CLI to start. A test points this at the fake; a machine at a custom install.</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Passed as <c>-m</c>; empty = the CLI's own default.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// For a local engine only: what to tell it about thinking. Empty or <c>engine</c> sends nothing.
    /// See <c>PanelSettings.LocalReasoningEffort</c> for why the default there is <c>none</c>.
    /// </summary>
    public string ReasoningEffort { get; init; } = string.Empty;

    /// <summary>Empty = the CLI's own authentication (the normal case for codex and gemini).</summary>
    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>One reviewer launch, fully described: the process, and where its answer lands.</summary>
/// <param name="OutputFile">Codex writes its final message here (<c>-o</c>); empty = read stdout.</param>
/// <param name="Adapter">
/// The vendor adapter that built this launch, so reading the answer and the usage stays with the
/// vendor that knows their shape instead of becoming a switch in the executor.
/// </param>
public sealed record ReviewerInvocation(
    string Provider,
    ReviewRole Role,
    ProcessRequest Request,
    string OutputFile = "",
    IReviewerRuntime? Adapter = null);

/// <summary>
/// THE vendor adapter: everything one AI vendor needs to plug into the panel, in one interface —
/// how to launch it, where its answer lands, and what the run consumed. A new vendor is one class
/// implementing this (usually only <see cref="Build"/>; the read side has working defaults),
/// registered in <see cref="ReviewerRuntimeSelector.Default"/> — nothing else changes.
/// </summary>
/// <remarks>
/// Modelled on rag_qln's AgentRuntimes: one class per vendor, refusal over default. `Build` is
/// pure — no IO — so every flag is a unit test; the read-side methods only look at what the
/// finished process left behind.
/// </remarks>
public interface IReviewerRuntime
{
    string Provider { get; }

    ReviewerInvocation Build(ReviewRole role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings);

    /// <summary>
    /// The command to start when the operator configured no path — and what `providers` probes.
    /// </summary>
    /// <remarks>
    /// It lives on the adapter because it is vendor knowledge, and duplicating it in the probe is
    /// how `providers` came to report a healthy reviewer as missing: the Antigravity installer
    /// puts `agy` on the PATH only for shells started afterwards, the adapter knew to fall back to
    /// the install location, and the probe did not.
    /// </remarks>
    string DefaultExecutable => Provider;

    /// <summary>
    /// The answer text out of a finished run. Default: the file the invocation named (codex's
    /// <c>-o</c>), stdout otherwise (gemini) — override when the CLI wraps its answer (claude's
    /// JSON envelope). Null means "no answer where this vendor puts one" — unparseable, by name.
    /// </summary>
    string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result) =>
        ReviewerOutput.FileOrStdout(invocation, result);

    /// <summary>
    /// What the run consumed, from the CLI's OWN reporting — tokens always when the vendor says,
    /// money only when the vendor itself prices the run (claude does; estimating for the rest
    /// would mean shipping a price table that is wrong within a month). Default: the schema-less
    /// scan over stdout, which reads codex's event stream and gemini's stats envelope alike.
    /// </summary>
    Usage ReadUsage(ReviewerInvocation invocation, ProcessResult result) =>
        UsageParser.Parse(result.StdOut);
}

/// <summary>The default read conventions the adapters share.</summary>
public static class ReviewerOutput
{
    /// <summary>The `-o` file when one was named, stdout otherwise. A named file that is missing
    /// or unreadable is null — the CLI exited 0 but wrote no answer.</summary>
    public static string? FileOrStdout(ReviewerInvocation invocation, ProcessResult result)
    {
        if (invocation.OutputFile.Length == 0)
        {
            return result.StdOut;
        }

        try
        {
            return File.ReadAllText(invocation.OutputFile);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// <c>codex exec</c>: read-only sandbox, ephemeral session, schema-bound answer written to a file.
/// Every one of these flags was verified against codex-cli 0.147.0 before it was written here.
/// </summary>
/// <param name="id">
/// The VENDOR this reviewer answers for, which is what every finding, usage line and vault-key lookup
/// is filed under. It defaults to the runtime's own name so the bare constructor keeps meaning what
/// it always did — but a vendor called <c>second-codex</c> must not report itself as <c>codex</c>:
/// two rows on one runtime then share one provider/role key, and the round's dictionary throws on
/// the duplicate before any model is reached. Reported as "every round dies on a duplicate
/// reviewer key".
/// </param>
public class CodexRuntime(string id = "codex") : IReviewerRuntime
{
    public virtual string Provider => id;

    private protected virtual string KeyVariable => "OPENAI_API_KEY";

    private protected virtual IEnumerable<string> ProviderOverrides => [];

    public ReviewerInvocation Build(ReviewRole role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings)
    {
        var outputFile = Path.Combine(outputDir, $"{Provider}-{role}.json");
        var request = new ProcessRequest(
            Executable(settings, "codex"),
            [
                "exec",
                "-s", "read-only",
                "--ephemeral",
                "--skip-git-repo-check",
                "--color", "never",
                "-C", worktreePath,
                "--output-schema", schemaFilePath,
                "-o", outputFile,
                // The event stream on stdout — the final answer still arrives via `-o`. This is
                // where codex reports what the run consumed (token_count events); without it the
                // only usage trace is a human-formatted stderr line.
                "--json",
                .. ModelArgs(settings),
                .. ProviderOverrides,
                // `-` is codex's documented "read the instructions from stdin". The prompt does
                // NOT travel in argv: on Windows this is an npm .cmd shim, and cmd.exe truncates
                // an argument at its first newline — silently, so the model simply answers as if
                // it had been given nothing.
                "-",
            ],
            worktreePath)
        {
            Environment = KeyEnv(KeyVariable, settings),
            StdIn = prompt,
            Timeout = settings.Timeout,
        };
        return new ReviewerInvocation(Provider, role, request, outputFile, this);
    }

    private protected static string Executable(ReviewerSettings settings, string fallback) =>
        settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : fallback;

    private protected static IEnumerable<string> ModelArgs(ReviewerSettings settings) =>
        settings.Model.Length > 0 ? ["-m", settings.Model] : [];

    private protected static Dictionary<string, string?> KeyEnv(string variable, ReviewerSettings settings) =>
        settings.ApiKey.Length > 0 ? new() { [variable] = settings.ApiKey } : [];
}

/// <summary>
/// DeepSeek has no CLI of its own — it rides the Codex runtime with a custom model provider
/// pointed at its OpenAI-compatible endpoint. One runtime, config-shifted; the key is REQUIRED
/// because there is no signed-in fallback to fall back to.
/// </summary>
public sealed class DeepseekRuntime() : CodexRuntime("deepseek")
{

    private protected override string KeyVariable => "DEEPSEEK_API_KEY";

    private protected override IEnumerable<string> ProviderOverrides =>
    [
        "-c", "model_provider=deepseek",
        "-c", "model_providers.deepseek.name=DeepSeek",
        "-c", "model_providers.deepseek.base_url=https://api.deepseek.com/v1",
        "-c", "model_providers.deepseek.env_key=DEEPSEEK_API_KEY",
    ];
}

/// <summary>
/// <c>gemini -p</c>: headless, JSON output, plan mode (read-only). Verified against gemini 0.55.1.
/// The answer arrives on stdout inside Gemini's envelope — <c>GeminiPayload</c> takes it apart.
/// </summary>
public sealed class GeminiRuntime(string id = "gemini") : IReviewerRuntime
{
    public string Provider => id;

    public ReviewerInvocation Build(ReviewRole role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings)
    {
        var request = new ProcessRequest(
            settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : "gemini",
            [
                // The review itself arrives on stdin, which gemini appends its `-p` text to — so
                // `-p` carries only this one short line. Same reason as codex: the Windows shim
                // is a .cmd, and cmd.exe would truncate a multi-line prompt at its first newline.
                "-p", "Follow the review instructions provided above and answer with the JSON only.",
                "-o", "json",
                // A round's worktree is a fresh directory, so it is NEVER a trusted folder — and
                // without trust Gemini refuses headless entirely (exit 55) AND overrides
                // --approval-mode back to "default", which is what the first real run hit on all
                // three of its reviewers. Trusting is safe precisely because plan mode is
                // read-only: the flag restores the restriction rather than removing one.
                "--skip-trust",
                "--approval-mode", "plan",
                ..settings.Model.Length > 0 ? (string[])["-m", settings.Model] : [],
            ],
            worktreePath)
        {
            Environment = settings.ApiKey.Length > 0
                ? new Dictionary<string, string?> { ["GEMINI_API_KEY"] = settings.ApiKey }
                : new Dictionary<string, string?>(),
            StdIn = prompt,
            Timeout = settings.Timeout,
        };
        return new ReviewerInvocation(Provider, role, request, OutputFile: string.Empty, this);
    }
}

/// <summary>An unknown provider is a refusal naming the catalog, never a default —
/// a silently wrong vendor shows up as a surprise invoice, not an error.</summary>
public sealed class ReviewerRuntimeSelector(IEnumerable<IReviewerRuntime> runtimes)
{
    private readonly Dictionary<string, IReviewerRuntime> _byProvider =
        runtimes.ToDictionary(r => r.Provider, StringComparer.OrdinalIgnoreCase);

    public static ReviewerRuntimeSelector Default { get; } =
        new([new CodexRuntime(), new GeminiRuntime(), new ClaudeRuntime(), new AntigravityRuntime(), new DeepseekRuntime()]);

    /// <summary>
    /// Every runtime NAME a configured vendor may name. The one list; nobody writes a second.
    /// </summary>
    /// <remarks>
    /// <para>It exists because two hand-written copies of this set both forgot the same entry. The
    /// extension had one that `vendorsFrom` validated against, and adding `local` to the type and
    /// not to it made every saved local reviewer come back as a codex one. The server had another
    /// in <c>PanelSettings.RuntimeOf</c>, and the same omission there turned a local vendor into a
    /// codex vendor with a base URL — which then failed the key check and was dropped from every
    /// round, so the panel showed a configured reviewer and the round opened with zero.</para>
    /// <para>Both were found by running a local model, days apart, and neither was reported by
    /// anything. A set that must be repeated is a set that will be repeated wrongly.</para>
    /// </remarks>
    public static readonly IReadOnlySet<string> RuntimeNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "codex", "gemini", "claude", "antigravity", "local",
        };

    public IReadOnlyCollection<string> Providers => _byProvider.Keys;

    /// <summary>
    /// A runtime by NAME, answering for a VENDOR. The two are different things and used to be
    /// conflated: <c>Named("claude")</c> returned a runtime that called itself "claude" whatever row
    /// had asked for it, so <c>my-claude</c> filed everything under a different row's name and two
    /// rows on one runtime collided on the round's provider/role key. Local and custom runtimes
    /// always took the id; the built-ins now do too.
    /// </summary>
    public static IReviewerRuntime? Named(string runtime, string vendorId) => runtime switch
    {
        "gemini" => new GeminiRuntime(vendorId),
        "claude" => new ClaudeRuntime(vendorId),
        "antigravity" => new AntigravityRuntime(vendorId),
        "codex" => new CodexRuntime(vendorId),
        "local" => new LocalRuntime(vendorId, string.Empty),
        _ => null,
    };

    public IReviewerRuntime? Find(string provider) => _byProvider.GetValueOrDefault(provider);

    public string RefusalFor(string provider) =>
        $"unknown provider '{provider}' — this build knows: {string.Join(", ", _byProvider.Keys.Order())}";
}
