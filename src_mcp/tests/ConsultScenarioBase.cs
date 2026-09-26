using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The consult scenario's harness — a throwaway repository with uncommitted work, a data directory,
/// the fake CLI standing in for codex, and the caller's environment pinned — shared by every suite that
/// drives the whole `consult` flow.
/// </summary>
/// <remarks>
/// Extracted from <c>ConsultScenarioTests</c> when the cadence kinds arrived
/// (<c>research/PLAN_consult_on_a_cadence.md</c>, epic 2): that file was already past the 800-line ceiling,
/// and a second suite copying its harness would have been the second copy the reuse rule forbids.
/// Every derived class belongs to the <c>fakecli-env</c> collection, for the reason
/// <see cref="CallingAs"/> gives.
/// </remarks>
public abstract class ConsultScenarioBase : IAsyncLifetime
{
    protected const string Advice = "Your parser counts the separator as a field. Check it: run the suite with a two-field input and print the count.";

    protected readonly ProcessLauncher _launcher = new();
    protected string _repo = string.Empty;
    protected string _data = string.Empty;

    protected static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-consult-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-consult-data-").FullName;
        await Git("init", "-b", "main");
        await Git("config", "user.email", "t@example.com");
        await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Parser.cs"), "int Count() => 3;\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        // The uncommitted work the consultant is asked about — collected by the SERVER, never handed in.
        await File.WriteAllTextAsync(Path.Combine(_repo, "Parser.cs"), "int Count() => 3; // was 4\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Scratch.cs"), "// a file nobody committed\n");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Answer("0198f2c1-first", Advice);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR", "FAKECLI_SLEEP_MS"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Script the stand-in: a thread id on its event stream, the advice in its `-o` file.</summary>
    protected static void Answer(string threadId, string advice)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", $$"""{"type":"thread.started","thread_id":"{{threadId}}"}""" + "\n");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", advice);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", null);
    }

    protected async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
    }

    /// <param name="providers">The REVIEWER rows — by default the one codex row the legacy-shaped shipped map borrows from.</param>
    /// <param name="consultants">The caller map — by default the shipped one, four legacy references.</param>
    /// <param name="launcher">
    /// The launcher the service runs every process through — by default the real one. A test that must
    /// act at a moment INSIDE a turn (after the record says <c>asking</c>, before the consultant answers)
    /// hands in a decorator of the real one; that seam is the only deterministic way to that moment.
    /// </param>
    /// <param name="reviewerTimeout">The launch's own deadline, which the turn's is derived from.</param>
    protected PanelService Service(
        int turns = 5,
        int callsPerSession = 10,
        bool enabled = true,
        IReadOnlyList<ProviderSettings>? providers = null,
        IReadOnlyDictionary<string, ConsultantChoice>? consultants = null,
        Serilog.ILogger? log = null,
        IProcessLauncher? launcher = null,
        TimeSpan? reviewerTimeout = null) => new(
        new PanelSettings
        {
            Providers = providers ?? [new("codex") { ExecutablePath = FakeCliExe }],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _data,
            ReviewerTimeout = reviewerTimeout ?? TimeSpan.FromSeconds(30),
            ConsultTurns = turns,
            ConsultCallsPerSession = callsPerSession,
            ConsultEnabled = enabled,
            Consultants = consultants ?? ConsultantRouting.Shipped,
        },
        VaultKeys.None("no vault in tests"),
        default,
        launcher ?? _launcher,
        log ?? Logger.None, Noticing.None);

    protected static readonly string[] CallerVariables =
        ["COAI_CALLER_SESSION", "CLAUDE_CODE_SESSION_ID", "CODEX_SESSION_ID", "GEMINI_CLI_SESSION_ID"];

    /// <summary>
    /// Pins WHICH vendor is calling for one test, and puts the process environment back afterwards.
    /// </summary>
    /// <remarks>
    /// The service reads the caller kind from the process environment, and this suite runs under
    /// whatever launched it: a Claude Code session exports <c>CLAUDE_CODE_SESSION_ID</c> to every
    /// child, so a test that assumed the kind was <c>other</c> would pass in one terminal and fail in
    /// another. Every vendor variable is cleared and exactly one is set; the caller's id follows from
    /// the same variable, so a consultation opened and resumed inside one test has one owner.
    /// <para><b>Mutating the PROCESS environment is safe here only because of the collection this class
    /// is in</b>, which is worth writing down rather than leaving to be rediscovered. xUnit runs one
    /// collection's classes sequentially, and every suite that touches these variables — this one,
    /// <c>SplitOrderTests</c>, <c>McpContractTests</c> — is in <c>fakecli-env</c>. The two that merely
    /// mention the names, <c>CallerSessionsTests</c> and <c>TheRoundKnowsWhoCalledItTests</c>, never
    /// read the environment at all: they hand <c>CallerIdentity.From</c> a lookup of their own, which
    /// is why they need no collection. A new class that reads the real environment joins
    /// <c>fakecli-env</c> or races this one — and the symptom would be the worst kind, passing alone
    /// and failing in the suite. (gemini, B3's code round; the answer was already here and unsaid.)</para>
    /// </remarks>
    protected static RestoredEnvironment CallingAs(string variable)
    {
        var saved = CallerVariables.Select(name => (name, Environment.GetEnvironmentVariable(name))).ToList();
        foreach (var name in CallerVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Environment.SetEnvironmentVariable(variable, "consult-scenario");

        return new RestoredEnvironment(saved);
    }

    protected sealed record RestoredEnvironment(IReadOnlyList<(string Name, string? Value)> Saved) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (name, value) in Saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    protected async Task<JsonElement> Consult(PanelService service, string problem, string id = "", string files = "[]") =>
        JsonDocument.Parse(await service.ConsultAsync(_repo, problem, files, id, TestContext.Current.CancellationToken)).RootElement;

    /// <summary>The plan an ordered (cadence or risk) consultation names, unless a test says otherwise.</summary>
    protected const string OrderedPlan = "todo/PLAN_x.md";

    /// <summary>A consultation FOR something: a group of epics (<c>cadence</c>) or one risky piece (<c>risk</c>).</summary>
    protected async Task<JsonElement> ConsultFor(PanelService service, string kind, string epics, string problem = "is this group right?", string id = "", string plan = OrderedPlan) =>
        JsonDocument.Parse(await service.ConsultAsync(_repo, problem, "[]", id, kind, plan, epics, TestContext.Current.CancellationToken)).RootElement;

    protected static string Advise(JsonElement reply) => reply.GetProperty("advice").GetString()!;

    protected static string Refusal(JsonElement reply) => reply.GetProperty("error").GetString()!;

    protected async Task<JsonElement> Close(PanelService service, string id, string outcome, string note = "") =>
        JsonDocument.Parse(await service.CloseConsultAsync(_repo, id, outcome, note, TestContext.Current.CancellationToken)).RootElement;
}
