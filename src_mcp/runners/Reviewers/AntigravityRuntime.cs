using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The Antigravity CLI (<c>agy</c>) — Google's replacement for Gemini Code Assist, and the best
/// fit for this product's contract of any vendor here.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> On 2026-08-31 the Gemini CLI stopped working on this machine with
/// <c>throwIneligibleOrProjectIdError</c> during <c>_doSetupUser</c>: "Code Assist for individuals.
/// To continue using Gemini, please migrate to the Antigravity suite of products". That is not a
/// quota, a timeout or an untrusted folder — three things it was mistaken for — it is Google
/// retiring the path. The migration is a different CLI, so it is a different adapter.</para>
/// <para><b>Why it fits.</b> It is the only vendor here that takes our finding schema as a flag
/// AND reports its own token usage AND has a reasoning-effort setting: <c>--json-schema</c> puts
/// the schema JSON straight into <c>result.response</c>, <c>usage</c> comes back on the same
/// envelope, and the model ids carry their own effort (<c>gemini-3.7-flash-high</c>). One
/// subscription also reaches Claude and GPT-OSS models, so a "vendor" here is a fleet.</para>
/// <para><b>Why the prompt rides stream-json.</b> <c>--print</c> takes its prompt as a flag VALUE,
/// and a review prompt is ~33 KB — past the ~32 KB Windows command line, where it would be
/// truncated or refused. <c>--input-format stream-json</c> reads NDJSON from stdin instead, one
/// message per line, which has no size limit. The shape was not guessed: the CLI named each
/// missing field in turn until it accepted
/// <c>{"event":"user","message":{"role":"user","content":"..."}}</c>.</para>
/// </remarks>
public sealed class AntigravityRuntime(string id = "antigravity") : IReviewerRuntime
{
    public string Provider => id;

    /// <summary>Flash at high effort: the operator's own choice, and the CLI's active model.</summary>
    public const string DefaultModel = "gemini-3.7-flash-high";

    public ReviewerInvocation Build(
        string role,
        string prompt,
        string worktreePath,
        string schemaFilePath,
        string outputDir,
        ReviewerSettings settings)
    {
        var request = new ProcessRequest(
            Executable(settings),
            [
                // Empty ON PURPOSE: the flag is mandatory even in stream mode, and a value here
                // would be refused ("a prompt given on the command line would be ignored").
                "--print=",
                "--input-format", "stream-json",
                "--output-format", "stream-json",
                // Read-only. The reviewer must not be able to edit the tree it is judging.
                "--mode", "plan",
                "--json-schema", schemaFilePath,
                .. Model(settings),
                .. Workspace(worktreePath),
            ],
            worktreePath)
        {
            StdIn = UserMessage(prompt),
            Environment = settings.ApiKey.Length > 0
                ? new Dictionary<string, string?> { ["ANTIGRAVITY_API_KEY"] = settings.ApiKey }
                : new Dictionary<string, string?>(),
            Timeout = settings.Timeout,
        };
        return new ReviewerInvocation(Provider, role, request, string.Empty, this, Model: settings.Model);
    }

    /// <summary>One NDJSON line. Serialised, never interpolated — a prompt contains quotes.</summary>
    private static string UserMessage(string prompt) => AntigravityStream.UserMessage(prompt);

    /// <summary>
    /// A reviewer whose shell command was auto-denied, asked again in the SAME conversation — issue #504.
    /// </summary>
    /// <remarks>
    /// <para>Headless <c>--mode plan</c> cannot ask a person whether <c>run_command</c> may run, so the CLI
    /// denies it and ENDS the turn with an empty response. There is no flag that takes the tool away,
    /// <c>--sandbox</c> changes nothing, and only the person's own global <c>settings.json</c> can allow a
    /// command — which this product will not write. A fresh repair meets the same denial; the same
    /// conversation, told the command will not come, answered with the schema's JSON on the real CLI.</para>
    /// <para>Everything else of the first launch is kept — read-only mode, the schema, the model, the
    /// workspace — so the follow-up can do nothing the first launch could not.</para>
    /// </remarks>
    public ReviewerInvocation? FollowUp(ReviewerInvocation first, string transcript)
    {
        var conversation = AntigravityStream.WasDenied(transcript) ? AntigravityStream.ConversationId(transcript) : string.Empty;

        return conversation.Length == 0
            ? null
            : first with
            {
                Request = first.Request with
                {
                    Arguments = [.. first.Request.Arguments, "--conversation", conversation],
                    StdIn = UserMessage(NoCommands),
                },
            };
    }

    /// <summary>What the continued conversation is told. Measured: it answers, and says what it could not check.</summary>
    internal const string NoCommands =
        "Shell commands are not available in this review: run_command was denied and will stay denied. Do not "
        + "call it again. Everything you need is in the review above and in the files you can read. Answer now "
        + "with the JSON the schema asks for; if a check needed a command, say in `notes` which one could not be run.";

    /// <summary>
    /// The plan stage runs in an empty scratch directory and must NOT be given a workspace: an
    /// agentic CLI handed a directory goes and reads it, which is what made plan rounds ten
    /// minutes long the first time.
    /// </summary>
    private static IEnumerable<string> Workspace(string worktreePath) =>
        Directory.Exists(worktreePath) && Directory.EnumerateFileSystemEntries(worktreePath).Any()
            ? ["--add-dir", worktreePath]
            : [];

    private static IEnumerable<string> Model(ReviewerSettings settings) =>
        ["--model", settings.Model.Length > 0 ? settings.Model : DefaultModel];

    /// <summary>
    /// The installer puts <c>agy</c> on the PATH, but only for shells started afterwards — and an
    /// MCP server is usually one that was started before. So the well-known install location is a
    /// fallback rather than a guess.
    /// </summary>
    public string DefaultExecutable => AntigravityStream.InstalledExecutable;

    private string Executable(ReviewerSettings settings) =>
        settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : DefaultExecutable;

    /// <summary>The answer is the `result` event's response — already the schema's JSON.</summary>
    public string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result) =>
        Result(result.StdOut) is { } r && r.TryGetProperty("response", out var response)
            ? response.GetString()
            : null;

    /// <summary>
    /// Usage off the same envelope. <c>thinking_tokens</c> sits INSIDE <c>output_tokens</c> and
    /// <c>cache_read_tokens</c> inside <c>input_tokens</c> — proved by the CLI's own
    /// <c>total_tokens</c>, which equals input + output exactly. Adding either would double-bill.
    /// </summary>
    public Usage ReadUsage(ReviewerInvocation invocation, ProcessResult result)
    {
        if (Result(result.StdOut) is not { } r ||
            !r.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object)
        {
            return Usage.None;
        }

        return new Usage(Number(usage, "input_tokens"), Number(usage, "output_tokens"), CostUsd: null);
    }

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    /// <summary>The `result` event out of the NDJSON stream — the last word, whatever preceded it.</summary>
    private static JsonElement? Result(string stdout) => AntigravityStream.Result(stdout);
}

/// <summary>
/// The <c>agy</c> stream-json wire format, shared by everything that drives that CLI.
/// </summary>
/// <remarks>
/// Two callers now: the reviewer adapter and the translator. The translator reached the point of
/// needing this on the day the Gemini CLI was retired, and the tempting move was a second NDJSON
/// line beside a second parser: that is how the two would have drifted. One shape, one parser.
/// </remarks>
public static class AntigravityStream
{
    /// <summary>The flags every launch shares: read-only, prompt on stdin, NDJSON both ways.</summary>
    /// <remarks>
    /// <c>--print</c> takes its prompt as a flag VALUE and a review prompt is ~33 KB, past the
    /// ~32 KB Windows command line. The empty <c>--print=</c> is mandatory even in stream mode.
    /// </remarks>
    public static string[] StreamingFlags =>
        ["--print=", "--input-format", "stream-json", "--output-format", "stream-json", "--mode", "plan"];

    /// <summary>Where the installer puts it, for a shell that started before the install ran.</summary>
    public static string InstalledExecutable
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var installed = Path.Combine(localAppData, "agy", "bin", "agy.exe");
            return OperatingSystem.IsWindows() && File.Exists(installed) ? installed : "agy";
        }
    }

    /// <summary>One NDJSON line. Serialised, never interpolated: a prompt contains quotes.</summary>
    public static string UserMessage(string prompt) =>
        JsonSerializer.Serialize(
            new StreamMessage("user", new StreamContent("user", prompt)),
            AntigravityJson.Default.StreamMessage) + "\n";

    /// <summary>The `result` event out of the NDJSON stream, whatever preceded it.</summary>
    public static JsonElement? Result(string stdout)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("event", out var name) &&
                    name.GetString() == "result" &&
                    document.RootElement.TryGetProperty("result", out var payload))
                {
                    return payload.Clone();
                }
            }
            catch (JsonException)
            {
                // A line that is not protocol is not a failure of the round.
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the CLI auto-denied a tool it could not ask about — its stderr sentence, or the stream's
    /// <c>denied_actions</c>, whichever the version prints.
    /// </summary>
    /// <remarks>A NON-EMPTY <c>denied_actions</c>: an empty list is a turn that was denied nothing.</remarks>
    public static bool WasDenied(string transcript) =>
        transcript.Contains("auto-denied", StringComparison.Ordinal)
        || transcript.Contains("\"denied_actions\":[{", StringComparison.Ordinal);

    /// <summary>The conversation the stream's `init` event names, or empty.</summary>
    public static string ConversationId(string transcript)
    {
        foreach (var line in transcript.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Init(line) is { } id)
            {
                return id;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The conversation an `init` event names — read by its PROPERTIES, in any key order, and only a
    /// value that looks like an id. A number, or anything else, is no id: the follow-up is skipped rather
    /// than an exception escaping the executor's failure path. (codex and our own reviewer, the code round.)
    /// </summary>
    private static string? Init(string line)
    {
        if (!line.StartsWith('{') || !line.Contains("\"init\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            return IdOf(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? IdOf(JsonElement root)
    {
        var isInit = root.TryGetProperty("event", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "init";
        var id = isInit && root.TryGetProperty("conversation_id", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

        return ConversationIdShape.IsMatch(id) ? id : null;
    }

    /// <summary>What a conversation id looks like: it becomes an argument, so nothing else passes.</summary>
    private static readonly System.Text.RegularExpressions.Regex ConversationIdShape =
        new("^[A-Za-z0-9_-]{1,128}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The text of a `result` event, or nothing: how a translated sentence is read.</summary>
    public static string? Response(string stdout) =>
        Result(stdout) is { } r && r.TryGetProperty("response", out var response)
            ? response.GetString()
            : null;
}

/// <summary>The stdin message shape the CLI accepts, discovered by its own error messages.</summary>
public sealed record StreamMessage(string Event, StreamContent Message);

public sealed record StreamContent(string Role, string Content);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower)]
[System.Text.Json.Serialization.JsonSerializable(typeof(StreamMessage))]
internal sealed partial class AntigravityJson : System.Text.Json.Serialization.JsonSerializerContext;
