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

    /// <summary>Empty = the CLI's own authentication (the normal case for codex and gemini).</summary>
    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>One reviewer launch, fully described: the process, and where its answer lands.</summary>
/// <param name="OutputFile">Codex writes its final message here (<c>-o</c>); empty = read stdout.</param>
public sealed record ReviewerInvocation(string Provider, ReviewRole Role, ProcessRequest Request, string OutputFile = "");

/// <summary>
/// Turns a role + prompt + worktree into an argv — one class per vendor, refusal over default,
/// modelled on rag_qln's AgentRuntimes. Pure: no IO here, so every flag is a unit test.
/// </summary>
public interface IReviewerRuntime
{
    string Provider { get; }

    ReviewerInvocation Build(ReviewRole role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings);
}

/// <summary>
/// <c>codex exec</c>: read-only sandbox, ephemeral session, schema-bound answer written to a file.
/// Every one of these flags was verified against codex-cli 0.147.0 before it was written here.
/// </summary>
public class CodexRuntime : IReviewerRuntime
{
    public virtual string Provider => "codex";

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
        return new ReviewerInvocation(Provider, role, request, outputFile);
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
public sealed class DeepseekRuntime : CodexRuntime
{
    public override string Provider => "deepseek";

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
public sealed class GeminiRuntime : IReviewerRuntime
{
    public string Provider => "gemini";

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
        return new ReviewerInvocation(Provider, role, request);
    }
}

/// <summary>An unknown provider is a refusal naming the catalog, never a default —
/// a silently wrong vendor shows up as a surprise invoice, not an error.</summary>
public sealed class ReviewerRuntimeSelector(IEnumerable<IReviewerRuntime> runtimes)
{
    private readonly Dictionary<string, IReviewerRuntime> _byProvider =
        runtimes.ToDictionary(r => r.Provider, StringComparer.OrdinalIgnoreCase);

    public static ReviewerRuntimeSelector Default { get; } =
        new([new CodexRuntime(), new GeminiRuntime(), new ClaudeRuntime(), new DeepseekRuntime()]);

    public IReadOnlyCollection<string> Providers => _byProvider.Keys;

    public IReviewerRuntime? Find(string provider) => _byProvider.GetValueOrDefault(provider);

    public string RefusalFor(string provider) =>
        $"unknown provider '{provider}' — this build knows: {string.Join(", ", _byProvider.Keys.Order())}";
}
