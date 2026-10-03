using CoaiMcp.Runners.Platform;
using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
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

        // Through the one continuation the consultant uses too. A review's first launch never carries
        // `--conversation`, so `Continue` APPENDS it here exactly as this method always did — the
        // reviewer's argv is byte for byte what #504 shipped. The id is asked the handle question FIRST:
        // `Continue` refuses a malformed one with an exception (a contract violation), and an exception
        // must never escape the executor's failure path — an id this check refuses gets no follow-up,
        // exactly as an id the stream's own shape check refuses always did.
        return ConsultantHandle.IsWellFormed(conversation)
            ? AntigravityStream.Continue(first, conversation, NoCommands)
            : null;
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
/// Three callers now: the reviewer adapter, the translator, and the consultant
/// (<c>AntigravityConsultant</c> — its prompt line, its denials and its continuation). The translator
/// reached the point of needing this on the day the Gemini CLI was retired, and the tempting move was a
/// second NDJSON line beside a second parser: that is how the two would have drifted. One shape, one
/// parser — and, since epic 1 of PLAN_the_consultant_works_on_every_vendor.md, one continuation.
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
    public static string InstalledExecutable =>
        InstalledExecutableFor(
            HostKinds.Current,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("PATH"),
            File.Exists);

    /// <summary>
    /// <see cref="InstalledExecutable"/> for a given host, home and file test — the installer's own path when the
    /// binary is there, else bare <c>agy</c> for the PATH to find.
    /// </summary>
    /// <remarks>
    /// <para>Windows: <c>%LOCALAPPDATA%\agy\bin\agy.exe</c>, where <c>install.ps1</c> puts it. Linux and WSL:
    /// <c>$HOME/.local/bin/agy</c>, where Google's <c>install.sh</c> puts it — the binary every WSL cell of the
    /// 2026-10-02 measurements ran (research/RESULTS_agy_allow_rule.md). A server started by an editor, or by a
    /// session that began before the installer ran, has a PATH without <c>~/.local/bin</c>, and bare <c>agy</c>
    /// then fails to start although the CLI is right there (E3.3 of
    /// PLAN_the_consultant_works_on_every_vendor.md). An <c>agy</c> the PATH already has WINS
    /// (<see cref="ExecutableResolver.OnPosixPath"/>): the home bin is the fallback for a PATH with none, never an
    /// override of the person's own install (epic 3's code round).</para>
    /// <para>macOS gets no guess: the same script supports it, but nothing here has run agy on a Mac, and a path
    /// nobody observed is not one to start a process from. The Linux path is joined with <c>/</c> rather than
    /// <c>Path.Combine</c>, because it is only ever a Linux path and a test on a Windows host must see the same
    /// string.</para>
    /// </remarks>
    public static string InstalledExecutableFor(HostKind host, string localAppData, string home, string? pathVariable, Func<string, bool> exists) =>
        host switch
        {
            HostKind.Windows => IfThere(Path.Combine(localAppData, "agy", "bin", "agy.exe"), exists),
            HostKind.Linux or HostKind.Wsl when ExecutableResolver.OnPosixPath("agy", pathVariable, exists).Length > 0 => "agy",
            HostKind.Linux or HostKind.Wsl => IfThere($"{home.TrimEnd('/')}/.local/bin/agy", exists),
            _ => "agy",
        };

    private static string IfThere(string installed, Func<string, bool> exists) => exists(installed) ? installed : "agy";

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

    /// <summary>
    /// The same launch, continuing <paramref name="conversation"/> with <paramref name="message"/> on stdin.
    /// </summary>
    /// <remarks>
    /// <para>Extracted from the reviewer's #504 follow-up when the consultant needed the identical move
    /// (PLAN_the_consultant_works_on_every_vendor.md, E1.2). The difference that made it a function rather
    /// than a second copy: a consultation's later turn ALREADY carries <c>--conversation</c>, because it
    /// resumes the vendor's own thread, and appending a second flag gave the CLI two ids to choose between —
    /// which one agy honours was never measured, and nothing here should depend on it. So an existing value
    /// is REPLACED in place, and the flag is appended only when there is none — which is every review, whose
    /// argv is therefore unchanged.</para>
    /// <para>Everything else is kept — read-only mode, the schema, the model, the workspace, the timeout —
    /// so a continuation can do nothing the first launch could not. A caller that must shorten it does so
    /// on the returned value.</para>
    /// <para><b>What it writes into an argv is checked where it is written</b>, as
    /// <c>ConsultantHandle</c>'s own remarks ask: the id must be a well-formed handle (a value starting
    /// <c>--</c> would read as a flag; one carrying a line break is cut by a Windows <c>.cmd</c> shim), and
    /// the argv it returns must carry no line break anywhere (<c>ConsultantLaunches.MustCarryNoLineBreak</c>,
    /// the guard every consultant <c>Build</c> already runs). A violation THROWS <see cref="ArgumentException"/>
    /// — a caller that reached here with an id it never validated has a defect, not a runtime case, which is
    /// how <c>ConsultantLaunches.MustBeLaunchable</c> treats a handle too. Both callers validate first: the
    /// consultant reads its id through <c>ConsultantHandle</c>, the reviewer asks it before calling.</para>
    /// </remarks>
    public static ReviewerInvocation Continue(ReviewerInvocation first, string conversation, string message)
    {
        if (!ConsultantHandle.IsWellFormed(conversation))
        {
            throw new ArgumentException("a conversation must be validated before it is continued", nameof(conversation));
        }

        var arguments = WithConversation(first.Request.Arguments, conversation);
        ConsultantLaunches.MustCarryNoLineBreak(arguments);

        return first with
        {
            Request = first.Request with
            {
                Arguments = arguments,
                StdIn = UserMessage(message),
            },
        };
    }

    /// <summary>The argv with exactly one <c>--conversation</c>, naming <paramref name="conversation"/>.</summary>
    /// <remarks>
    /// A flag found LAST, with no value after it, is replaced whole rather than given a second flag beside
    /// it: <c>Skip(at + 2)</c> past the end is simply nothing.
    /// </remarks>
    private static string[] WithConversation(IReadOnlyList<string> arguments, string conversation)
    {
        var at = arguments.ToList().IndexOf("--conversation");

        return at < 0
            ? [.. arguments, "--conversation", conversation]
            : [.. arguments.Take(at), "--conversation", conversation, .. arguments.Skip(at + 2)];
    }

    /// <summary>
    /// The ACTION words the stream's <c>result.denied_actions</c> names — <c>command</c>, <c>read_file</c>,
    /// <c>read_url</c> — read as JSON, in the order given; empty when nothing was denied.
    /// </summary>
    /// <remarks>
    /// <para>Measured 2026-10-02 (research/RESULTS_agy_allow_rule.md, RESULTS_agy_consult_follow_up.md):
    /// the list carries agy's PERMISSION words, not tool names — a denied <c>run_command</c> is
    /// <c>{"action":"command","display_name":"RunCommand"}</c>. And a denied step can report
    /// <c>state: DONE</c>, so step state is never read: this list is the stream's own verdict.</para>
    /// <para>Parsed rather than searched. <see cref="WasDenied"/> matches a literal
    /// <c>"denied_actions":[{</c>, which is right for the reviewer it was written for and wrong the moment a
    /// version prints a space after the colon. A word that does not look like one — anything but lower-case
    /// letters and underscores, or longer than a word has any reason to be — is dropped: it becomes part of
    /// a sentence the model is sent, and the stream is the vendor's to write.</para>
    /// </remarks>
    public static IReadOnlyList<string> DeniedActions(string stdout) =>
        Result(stdout) is { } result
        && result.TryGetProperty("denied_actions", out var denied)
        && denied.ValueKind == JsonValueKind.Array
            ? [.. denied.EnumerateArray().SelectMany(ActionWord).Distinct(StringComparer.Ordinal)]
            : [];

    /// <summary>
    /// The denied actions of one launch: the stream's list, and — for a version that prints only its
    /// stderr sentence — the permission that sentence names.
    /// </summary>
    /// <remarks>
    /// <para>The <c>auto-denied</c> sentence is matched in STDERR ONLY, the half
    /// <c>ReviewerExecutor.Complaint</c> reads, and never in stdout: stdout carries the model's own tool
    /// parameters, and a model that reads or greps for the words "auto-denied" — a consultant asked about
    /// this very code would — must not be taken for a denial. A sentence that names no permission names
    /// nothing to continue past, and the turn ends as the empty answer it is.</para>
    /// </remarks>
    public static IReadOnlyList<string> DeniedActions(string stdout, string stderr) =>
        [.. DeniedActions(stdout).Concat(StderrDenial(stderr)).Distinct(StringComparer.Ordinal)];

    private static IEnumerable<string> StderrDenial(string stderr) =>
        DeniedSentence.Match(stderr) is { Success: true } said && IsActionWord(said.Groups["action"].Value)
            ? [said.Groups["action"].Value]
            : [];

    private static IEnumerable<string> ActionWord(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("action", out var action)
        && action.ValueKind == JsonValueKind.String
            ? Word(action.GetString())
            : [];

    private static IEnumerable<string> Word(string? word) => word is { } said && IsActionWord(said) ? [said] : [];

    private static bool IsActionWord(string word) => ActionWordShape.IsMatch(word);

    /// <summary>What a permission word looks like: <c>command</c>, <c>read_file</c>. It becomes prose sent to the model.</summary>
    private static readonly System.Text.RegularExpressions.Regex ActionWordShape =
        new("^[a-z][a-z_]{0,39}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>agy 1.2.15's stderr sentence: <c>a tool required the "command" permission … so it was auto-denied.</c></summary>
    private static readonly System.Text.RegularExpressions.Regex DeniedSentence = new(
        "required the \"(?<action>[^\"\\r\\n]{1,64})\" permission[^\\r\\n]*auto-denied",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

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
