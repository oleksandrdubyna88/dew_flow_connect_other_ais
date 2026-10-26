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
/// A Kimi consultant check (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>) runs as a Kimi consultation's turn does: in a COPY of the
/// check's scratch repository, on a one-shot owned home under &lt;root&gt;/coai-vh/&lt;8&gt;, disposed junction-first whatever ended the check.
/// </summary>
/// <remarks>
/// The person is a <see cref="KimiFixture"/>, the host a <see cref="WindowsStandIn"/> with <see cref="KimiSeams"/> set for it, and the shim
/// is <see cref="PlayedKimi"/> — no turn is paid for and the real <c>kimi</c> is never started. Runs on every host: no skip.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    private static readonly IReadOnlyDictionary<string, ConsultantChoice> OnKimi = new Dictionary<string, ConsultantChoice>
    {
        [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi"),
    };

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _temp.Dispose();
        _data.Dispose();
    }

    /// <summary>The settings of a Kimi check; an empty <paramref name="homesRoot"/> is settings built by hand (decision 2).</summary>
    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = OnKimi,
    };

    /// <summary>The host, the person and the lock root this flow's Kimi adapter sees — the AKimiConsultationScenarioTests.StandIn shape, without a caller variable.</summary>
    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>The value after <paramref name="flag"/> in the shim's argv.</summary>
    private static string ValueOf(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    /// <summary>The played answer that reads the marker in the folder the shim was told to stand in, and declines the canary.</summary>
    private static ProcessResult Reads(ProcessRequest request) => PlayedKimi.Answered(
        PlayedKimi.SessionOf(request), 19973, 31,
        $"marker: {File.ReadAllText(Path.Combine(ValueOf(request, "--cwd"), "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT");

    /// <summary>A check of Kimi reads the marker in a copy of the scratch, on a one-shot home, and leaves the person's folder whole.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-kimi-check-homes-");
        var before = person.Hash();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => Reads(request) };

        var (code, said, _) = await Check(Settings(homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be(ConsultCheckStates.Answered, said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        Text(said, "canary").Should().NotBe(CanaryReadings.Read);
        Text(said, "failureKind").Should().BeEmpty("the scratch repository and its canary are unchanged — a change is tree-changed");
        Text(said, "confinement").Should().BeEmpty("decision 3: Kimi's confinement does not depend on its CLI's version");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        ValueOf(shim, "--cwd").Should().Contain(Path.Combine(ConsultantCheck.OwnName, "tree"), "a copy in the check's own scratch, never the repository");
        var home = ValueOf(shim, AcpWords.HomeFlag);
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(Path.Combine(homes, VibeHomes.OneShotFolder), "J3: <root>/coai-vh/<8>/kh-<10 hex>");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        shim.Arguments.Should().NotContain(AcpWords.SessionFlag, "a check is one shot");
        person.Hash().Should().Equal(before, "the person's folder is whole, bytes and ReadOnly");
    }

    /// <summary>After a check, answered or failed, its one-shot home is gone: the junction is unlinked before the deleter could meet it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool answered)
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-kimi-check-homes-");
        var before = person.Hash();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => answered
                ? Reads(request)
                : new ProcessResult(70, KimiShimScripts.Report(AcpEnd.AgentFailed, string.Empty, "the agent stopped", tokens: false, session: PlayedKimi.SessionOf(request)), "the agent stopped\r\n", TimedOut: false),
        };
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        var (_, said, _) = await Check(Settings(homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(answered ? ConsultCheckStates.Answered : ConsultCheckStates.Failed, said.ToString());
        var home = ValueOf(played.Shims.Should().ContainSingle().Subject.Request, AcpWords.HomeFlag);
        Directory.Exists(Path.GetDirectoryName(home)).Should().BeFalse("coai-vh/<8> goes when the check ends");
        Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty();
        _unlinkedByTheDeleter.Should().BeEmpty("KimiHome unlinked the junction first, so the deleter never met it");
        person.Hash().Should().Equal(before, "the person's folder is whole, bytes and ReadOnly");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch goes too");
    }

    /// <summary>A check cancelled mid-turn still disposes its one-shot home, junction first.</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-kimi-check-homes-");
        var before = person.Hash();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (request, ct) =>
            {
                started.TrySetResult(ValueOf(request, AcpWords.HomeFlag));
                await Task.Delay(Timeout.Infinite, ct);
            },
        };
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var checking = Check(Settings(homes), played, stop.Token);
        var home = await started.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        Directory.Exists(home).Should().BeTrue("the home is there while the turn runs");

        await stop.CancelAsync();
        var (_, said, _) = await checking;

        Text(said, "state").Should().Be(ConsultCheckStates.Failed, said.ToString());
        Text(said, "failureKind").Should().Be(ConsultFailureKinds.Cancelled);
        Directory.Exists(Path.GetDirectoryName(home)).Should().BeFalse("the finally disposes coai-vh/<8> on a cancel too");
        _unlinkedByTheDeleter.Should().BeEmpty("junction-first on a cancel too");
        person.Hash().Should().Equal(before);
    }

    /// <summary>With no homes root, the check's one-shot home is the copy's own place, and the scratch's deleter takes it junction-first.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var before = person.Hash();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => Reads(request) };
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        var (_, said, _) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(ConsultCheckStates.Answered, said.ToString());
        var home = ValueOf(played.Shims.Should().ContainSingle().Subject.Request, AcpWords.HomeFlag);
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "decision 2: <scratch>/own/kh-<10 hex>");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        home.Should().StartWith(_temp.Path + Path.DirectorySeparatorChar, "the check's scratch is under its temp root");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("ConsultCheckScratch.Delete took the scratch");
        _unlinkedByTheDeleter.Should().BeEmpty("ConsultCheckScratch.Delete disposed <root>/own junction-first (E5.3 g, R6)");
        person.Hash().Should().Equal(before);
    }
}
