using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// The scripted ACP agent of the fake CLI (<c>tests_fakecli/AcpAgent.cs</c>), as a test sees it: a script
/// written from step strings, a profile that launches it, a working folder, and the log of every line that crossed
/// the pipe — read back so a test asserts what the CLIENT SENT, not what it meant to.
/// </summary>
/// <remarks>
/// A real second process every time, never an in-process fake of the transport: the teardown under test is a tree
/// kill by pid, and the reverse requests are answered while a request is outstanding on a real pipe — neither has a
/// meaning inside one process.
/// </remarks>
internal sealed class FakeAcpAgent : IDisposable
{
    internal static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private readonly TempDir _dir = TempDir.For("coai-acp-fake-");

    public FakeAcpAgent(IEnumerable<string> steps, string extraFields = "")
    {
        Directory.CreateDirectory(Cwd);
        File.WriteAllText(Script, $$"""{"log":{{Quote(Log)}},"steps":[{{string.Join(",", steps)}}]{{extraFields}}}""");
    }

    /// <summary>Steps that need the agent itself — its pid file — written once it exists: <c>a =&gt; a.Grandchild()</c>.</summary>
    public FakeAcpAgent(string extraFields, params Func<FakeAcpAgent, string>[] steps)
    {
        Directory.CreateDirectory(Cwd);
        File.WriteAllText(Script, $$"""{"log":{{Quote(Log)}},"steps":[{{string.Join(",", steps.Select(step => step(this)))}}]{{extraFields}}}""");
    }

    public string Script => _dir.At("agent.json");

    public string Log => _dir.At("agent.log");

    public string PidFile => _dir.At("grandchild.pid");

    /// <summary>The folder the agent works in — the boundary's root in these tests.</summary>
    public string Cwd => _dir.At("work");

    /// <summary>A path OUTSIDE the working folder, beside it.</summary>
    public string Outside(string name) => _dir.At(name);

    public string Inside(params string[] parts) => Path.Combine([Cwd, .. parts]);

    /// <summary>Where the agent writes its environment and the probes of <see cref="EnvField"/>, when the script asks.</summary>
    public string EnvFile => _dir.At("env.txt");

    /// <summary>The script with more fields (each one `,"name":value`) — for a field that names a path of this very agent.</summary>
    public FakeAcpAgent WithFields(string extraFields)
    {
        var text = File.ReadAllText(Script).TrimEnd();
        File.WriteAllText(Script, text[..^1] + extraFields + "}");

        return this;
    }

    /// <summary>The script fields that make the agent announce exactly these values for the <c>model</c> option — none: no option at all.</summary>
    public static string Models(params string[] models) => $$""","models":[{{string.Join(",", models.Select(Quote))}}]""";

    /// <summary>The script field that makes the agent answer a <c>/usage</c> prompt with this text as one message chunk.</summary>
    public static string UsageReply(string text) => $$""","usageReply":{{Quote(text)}}""";

    /// <summary>The script field that makes the agent sleep this long on start, before it reads anything — a cold start a deadline can be raced against.</summary>
    public static string StartDelay(int ms) => $$""","startDelayMs":{{ms}}""";

    /// <summary>The script fields that make the agent write its environment to <see cref="EnvFile"/> at start, and the state of each lock file named.</summary>
    public string EnvField(params string[] probes) => $$""","envFile":{{Quote(EnvFile)}},"probes":[{{string.Join(",", probes.Select(Quote))}}]""";

    /// <summary>A profile that launches the fake agent by verb, with the exclusions and failure sentences a test names — and, when a test names one, the sentence its agent request is refused with.</summary>
    public IAcpProfile Profile(IReadOnlyList<string>? excluded = null, IReadOnlyList<AcpKnownFailure>? failures = null, string agentRefusal = "") =>
        new FakeAcpProfile(Script, excluded ?? KimiAcpProfile.Exclusions, failures ?? [], agentRefusal);

    public AcpAsk Ask(string prompt = "say hello", string model = "m-1", TimeSpan? deadline = null, string? executable = null) =>
        new(executable ?? FakeCliExe, Cwd, model, prompt, deadline ?? TimeSpan.FromSeconds(30));

    /// <summary>The grandchild's pid, once the <c>grandchild</c> step wrote it — polled, since the agent writes it when it gets there.</summary>
    public int GrandchildPid()
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(PidFile) && int.TryParse(File.ReadAllText(PidFile), out var pid))
            {
                return pid;
            }

            Thread.Sleep(25);
        }

        throw new TimeoutException("the fake agent never wrote its grandchild's pid");
    }

    /// <summary>
    /// The log's lines, read WITHOUT holding it against the agent: the agent appends to this file from its own process, and a reader that
    /// shares nothing (<c>File.ReadAllLines</c> shares read only) makes the agent's next append fail — the agent dies and the turn ends with
    /// a protocol error. A test that polls the log while the agent runs (<see cref="WhenReceivedAsync"/>) must therefore read it shared.
    /// </summary>
    private IEnumerable<string> LogLines()
    {
        using var stream = new FileStream(Log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Split('\n');
    }

    /// <summary>Every logged line, in order: direction and message.</summary>
    public IReadOnlyList<(string Dir, JsonElement Msg)> Entries() =>
        !File.Exists(Log)
            ? []
            : [.. LogLines()
                .Where(line => line.Length > 0)
                .Select(line => JsonDocument.Parse(line).RootElement.Clone())
                .Select(entry => (entry.GetProperty("dir").GetString()!, entry.TryGetProperty("msg", out var msg) ? msg : entry.GetProperty("raw")))];

    /// <summary>
    /// Completes once the CLIENT has sent <paramref name="method"/> — the agent logs a line when it READS it, so this is "the agent has the request",
    /// an event a test waits on instead of a clock. The log is read while the agent appends to it, so a read that meets the append is tried again.
    /// </summary>
    public async Task WhenReceivedAsync(string method, CancellationToken ct)
    {
        while (!ReceivedNow(method))
        {
            await Task.Delay(25, ct);
        }
    }

    private bool ReceivedNow(string method)
    {
        try
        {
            return Received(method).Count > 0;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // A line caught half-written: the next poll reads it whole.
            return false;
        }
    }

    /// <summary>The requests and notifications the CLIENT sent, by method, in order.</summary>
    public IReadOnlyList<JsonElement> Received(string method) =>
        [.. Entries().Where(e => e.Dir == "in" && MethodOf(e.Msg) == method).Select(e => e.Msg)];

    /// <summary>The methods the client sent, in order — initialize, session/new, …</summary>
    public IReadOnlyList<string> ReceivedMethods() =>
        [.. Entries().Where(e => e.Dir == "in" && MethodOf(e.Msg).Length > 0).Select(e => MethodOf(e.Msg))];

    /// <summary>The client's replies to the AGENT's own requests of that method, in order.</summary>
    public IReadOnlyList<JsonElement> RepliesTo(string method)
    {
        var entries = Entries();
        var ids = entries.Where(e => e.Dir == "out" && MethodOf(e.Msg) == method && e.Msg.TryGetProperty("id", out _))
            .Select(e => e.Msg.GetProperty("id").GetRawText()).ToHashSet(StringComparer.Ordinal);

        return [.. entries.Where(e => e.Dir == "in" && e.Msg.TryGetProperty("id", out var id) && MethodOf(e.Msg).Length == 0 && ids.Contains(id.GetRawText())).Select(e => e.Msg)];
    }

    public static string Quote(string text) => JsonValue.Create(text)!.ToJsonString();

    public static string Chunk(string text) => $$"""{"kind":"chunk","text":{{Quote(text)}}}""";

    public static string Usage(long used, long size) => $$"""{"kind":"usage","used":{{used}},"size":{{size}}}""";

    public static string ToolCall(string id, string title, string kind, string status) =>
        $$"""{"kind":"toolCall","id":{{Quote(id)}},"title":{{Quote(title)}},"toolKind":{{Quote(kind)}},"status":{{Quote(status)}}}""";

    public static string ToolCallUpdate(string id, string status) => $$"""{"kind":"toolCallUpdate","id":{{Quote(id)}},"status":{{Quote(status)}}}""";

    /// <param name="options">Pairs of (optionId, kind), offered in that order.</param>
    public static string Permission(string title, string kind, params (string Id, string Kind)[] options) =>
        $$"""{"kind":"permission","title":{{Quote(title)}},"toolKind":{{Quote(kind)}},"options":[{{string.Join(",", options.Select(Option))}}]}""";

    private static string Option((string Id, string Kind) option) =>
        $$"""{"optionId":{{Quote(option.Id)}},"kind":{{Quote(option.Kind)}},"name":{{Quote(option.Kind)}}}""";

    public static string Read(string path) => $$"""{"kind":"read","path":{{Quote(path)}}}""";

    /// <summary>A read whose reply is also said back as the agent's own message — the shape that would leak a served file into an answer.</summary>
    public static string ReadEchoed(string path) => $$"""{"kind":"read","path":{{Quote(path)}},"echo":true}""";

    public static string Read(string path, int line, int limit) => $$"""{"kind":"read","path":{{Quote(path)}},"line":{{line}},"limit":{{limit}}}""";

    public static string Request(string method, string paramsJson) => $$"""{"kind":"request","method":{{Quote(method)}},"params":{{paramsJson}}}""";

    public static string Stray(string text) => $$"""{"kind":"stray","text":{{Quote(text)}}}""";

    public static string Spew(int chars) => $$"""{"kind":"spew","chars":{{chars}}}""";

    /// <summary><paramref name="chars"/> characters written on the agent's STDERR, no newline — the protocol stream untouched.</summary>
    public static string StdErr(int chars) => $$"""{"kind":"stderr","chars":{{chars}}}""";

    /// <summary>The text <paramref name="text"/> written on the agent's STDERR as given — a vendor's own error line; end it with a line break for a complete line.</summary>
    public static string StdErrText(string text) => $$"""{"kind":"stderrText","text":{{Quote(text)}}}""";

    public const string AwaitCancel = """{"kind":"awaitCancel"}""";

    public static string Sleep(int ms) => $$"""{"kind":"sleep","ms":{{ms}}}""";

    public const string Hang = """{"kind":"hang"}""";

    public static string Exit(int code) => $$"""{"kind":"exit","code":{{code}}}""";

    /// <summary>A grandchild that calls <c>setsid()</c> first — in its own session and group, as <c>rg</c> is in a Grep turn (L-K4) — its pid in <paramref name="pidFile"/>.</summary>
    public static string GrandchildSetsid(string pidFile) => $$"""{"kind":"grandchildSetsid","pidFile":{{Quote(pidFile)}}}""";

    /// <summary>A grandchild that waits for the file <paramref name="trigger"/> and only THEN starts a <c>setsid</c> sleeper, writing ITS pid to <paramref name="pidFile"/>.</summary>
    public static string GrandchildLateSetsid(string trigger, string pidFile) => $$"""{"kind":"grandchildLateSetsid","trigger":{{Quote(trigger)}},"pidFile":{{Quote(pidFile)}}}""";

    /// <summary>A grandchild that writes its pid and exits 0 at once: a zombie until its parent reaps it.</summary>
    public static string GrandchildExits(string pidFile) => $$"""{"kind":"grandchildExits","pidFile":{{Quote(pidFile)}}}""";

    public string Grandchild() => $$"""{"kind":"grandchild","pidFile":{{Quote(PidFile)}}}""";

    public void Dispose() => _dir.Dispose();

    private static string MethodOf(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String
            ? method.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>The fake agent as a profile: the verb and the script in argv, the exclusions and failures a test chose.</summary>
    private sealed class FakeAcpProfile(string script, IReadOnlyList<string> excluded, IReadOnlyList<AcpKnownFailure> failures, string agentRefusal) : IAcpProfile
    {
        public string AgentRefusal(ProcessRequest request, AcpAsk ask) => agentRefusal;

        public string Name => "fake";

        public string DefaultExecutable => FakeCliExe;

        public IReadOnlyList<string> Arguments => ["acp-agent", script];

        public string ModelOptionId => "model";

        public IReadOnlyList<string> ExcludedNames => excluded;

        public IReadOnlyList<AcpKnownFailure> KnownFailures => failures;
    }
}
