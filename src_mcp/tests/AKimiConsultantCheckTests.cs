using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c> on a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs one Kimi turn as a
/// consultation does — in a COPY of the check's scratch, on a one-shot home, disposed junction-first on every ending. The fake person
/// (<see cref="KimiFixture"/>) and the played shim (<see cref="PlayedKimi"/>) are the same ones the Kimi consultation scenarios use; no turn is paid for.
/// </summary>
/// <remarks>
/// The helpers of <see cref="ConsultantCheckTests"/> are copied here unchanged (they are private there, and that class is not edited).
/// Every folder a test makes is the fixture's <c>testsToDelete-kimi-</c> folder or a <c>testsToDelete-kimi-check-</c> folder of its own.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private static readonly string[] Steering =
        ["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR", "FAKECLI_SLEEP_MS",
         "FAKECLI_RECORD_DIR", "FAKECLI_HELP_STDOUT", "FAKECLI_HELP_EXIT", "FAKECLI_SIDE_EFFECT", "FAKECLI_REPAIR_MARKER", "FAKECLI_VERSION_STDOUT",
         "FAKECLI_TURN1_REPAIR_STDOUT", "FAKECLI_TURN1_REPAIR_STDERR"];

    /// <summary>The variables that name the caller's session — the same list <c>ConsultScenarioBase</c> clears.</summary>
    private static readonly string[] CallerVariables =
        ["COAI_CALLER_SESSION", "CLAUDE_CODE_SESSION_ID", "CODEX_SESSION_ID", "GEMINI_CLI_SESSION_ID"];

    private readonly string _data = Directory.CreateTempSubdirectory("coai-checktest-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("coai-checktest-temp-").FullName;
    private readonly string _record = Directory.CreateTempSubdirectory("coai-checktest-argv-").FullName;
    private readonly List<string> _warned = [];

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public AKimiConsultantCheckTests()
    {
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
    }

    public void Dispose()
    {
        foreach (var name in Steering)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_data, _temp, _record])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    /// <summary>Several scopes, given back in reverse.</summary>
    private sealed class Scopes(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var scope in scopes.Reverse())
            {
                scope.Dispose();
            }
        }
    }

    /// <summary>
    /// The person, the host and the lock root this check's Kimi adapter sees — set by a SYNCHRONOUS call, so the values reach the test's own
    /// flow and every call it awaits (copied from <c>AKimiConsultationScenarioTests</c>).
    /// </summary>
    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))),
        CallingAs("CLAUDE_CODE_SESSION_ID"));

    /// <summary>Pins which vendor is calling for one test and puts the process environment back afterwards (copied from <c>ConsultScenarioBase</c>).</summary>
    private static RestoredEnvironment CallingAs(string variable)
    {
        var saved = CallerVariables.Select(name => (name, Environment.GetEnvironmentVariable(name))).ToList();
        foreach (var name in CallerVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Environment.SetEnvironmentVariable(variable, "consult-scenario");

        return new RestoredEnvironment(saved);
    }

    private sealed record RestoredEnvironment(IReadOnlyList<(string Name, string? Value)> Saved) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (name, value) in Saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private PanelSettings Settings(string runtime = "codex", TimeSpan? timeout = null, bool enabled = true) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = timeout ?? TimeSpan.FromSeconds(60),
        ConsultEnabled = enabled,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new(runtime, Runtime: runtime, ExecutablePath: FakeCliExe),
        },
    };

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct, params string[] args)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", .. args], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>The value after <paramref name="flag"/> in a launch's arguments.</summary>
    private static string ValueOf(ProcessRequest request, string flag)
    {
        var at = request.Arguments.ToList().IndexOf(flag);

        return request.Arguments[at + 1];
    }

    /// <summary>The marker the stand-in reads: the last word of the copy's <c>CHECK.md</c>, its full stop dropped.</summary>
    private static string MarkerIn(ProcessRequest request) =>
        File.ReadAllText(Path.Combine(ValueOf(request, "--cwd"), "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

    /// <summary>An answered turn that reads the marker from the copy it ran in, and says the canary cannot be read.</summary>
    private static ProcessResult AnswerFor(ProcessRequest request) =>
        PlayedKimi.Answered(PlayedKimi.SessionOf(request), 1000, 10, $"marker: {MarkerIn(request)}{Environment.NewLine}canary: CANNOT");

    /// <summary>A Kimi consultant row for the check, its homes root beside it.</summary>
    private PanelSettings KimiSettings(string homes) => Settings() with
    {
        ConsultantHomesRoot = homes,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("my-kimi", Model: string.Empty, Runtime: "kimi", ExecutablePath: PlayedKimi.Shim),
        },
    };

    /// <summary>Every file of the fake person's folder outside its lock root: its bytes and whether it is read-only.</summary>
    private static Dictionary<string, (byte[] Bytes, bool ReadOnly)> PersonSnapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(folder, path).StartsWith("locks" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToDictionary(
                path => Path.GetRelativePath(folder, path),
                path => (File.ReadAllBytes(path), File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly)));

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-kimi-check-");
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswerFor(request) };

        var (code, said, _) = await Check(KimiSettings(homes), played, TestContext.Current.CancellationToken, "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        Text(said, "canary").Should().Be("not-attempted");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        ValueOf(shim, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch");
        var home = ValueOf(shim, "--home");
        home.Should().StartWith(Path.Combine(homes, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar, "a one-shot home under the homes root");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home goes when the check ends");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool answered)
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-kimi-check-");
        var before = PersonSnapshot(person.Folder);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => answered
                ? AnswerFor(request)
                : new ProcessResult(70, KimiShimScripts.Report(AcpEnd.AgentFailed, string.Empty, "the agent failed", tokens: false, session: PlayedKimi.Minted), string.Empty, TimedOut: false),
        };
        var unlinked = new ConcurrentQueue<string>();
        var homeUnlinked = new ConcurrentQueue<string>();
        GitScratch.AfterUnlink = unlinked.Enqueue;
        KimiHome.AfterUnlink = homeUnlinked.Enqueue;
        try
        {
            var (code, said, _) = await Check(KimiSettings(homes), played, TestContext.Current.CancellationToken, "--caller", "claude");

            if (answered)
            {
                code.Should().Be(0);
                Text(said, "state").Should().Be("answered", said.ToString());
            }
            else
            {
                Text(said, "state").Should().NotBe("answered", said.ToString());
            }
        }
        finally
        {
            GitScratch.AfterUnlink = null;
            KimiHome.AfterUnlink = null;
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home is gone, answered or not");
        unlinked.Should().BeEmpty("the home's junction is unlinked before anything is deleted (E5.3 g)");
        homeUnlinked.Should().ContainSingle().Which.Should().EndWith(Path.Combine("home", "credentials"), "the home's one junction is unlinked before its folder is deleted (E5.3 g)");
        PersonSnapshot(person.Folder).Should().BeEquivalentTo(before, "the fake person's sign-in is untouched");
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-kimi-check-");
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };
        var unlinked = new ConcurrentQueue<string>();
        var homeUnlinked = new ConcurrentQueue<string>();
        GitScratch.AfterUnlink = unlinked.Enqueue;
        KimiHome.AfterUnlink = homeUnlinked.Enqueue;
        try
        {
            var checking = Check(KimiSettings(homes), played, cts.Token, "--caller", "claude");
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await cts.CancelAsync();

            var (_, said, _) = await checking;

            Text(said, "state").Should().Be("failed", said.ToString());
            Text(said, "failureKind").Should().Be(ConsultFailureKinds.Cancelled);
        }
        finally
        {
            GitScratch.AfterUnlink = null;
            KimiHome.AfterUnlink = null;
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home is gone, cancelled or not");
        unlinked.Should().BeEmpty("the home's junction is unlinked before anything is deleted (E5.3 g)");
        homeUnlinked.Should().ContainSingle().Which.Should().EndWith(Path.Combine("home", "credentials"), "the home's one junction is unlinked before its folder is deleted (E5.3 g)");
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswerFor(request) };
        var homeUnlinked = new ConcurrentQueue<string>();
        KimiHome.AfterUnlink = homeUnlinked.Enqueue;
        try
        {
            var (code, said, _) = await Check(KimiSettings(string.Empty), played, TestContext.Current.CancellationToken, "--caller", "claude");

            code.Should().Be(0);
            Text(said, "state").Should().Be("answered", said.ToString());
        }
        finally
        {
            KimiHome.AfterUnlink = null;
        }

        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        ValueOf(shim, "--home").Should().Contain(Path.Combine("own", "kh-"), "with no homes root the home is the scratch's own folder");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch goes with the check");
        homeUnlinked.Should().ContainSingle().Which.Should().EndWith(Path.Combine("home", "credentials"), "the home's one junction is unlinked before its folder is deleted (E5.3 g)");
    }
}
