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
/// <c>coai-mcp --check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs
/// one turn as a consultation makes it — in a COPY of the check's scratch repository, on a ONE-SHOT home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>
/// (or <c>&lt;scratch&gt;\own</c> when the settings carry no root) — and disposes the home junction-first whatever ended the check.
/// </summary>
/// <remarks>
/// The whole mode in process (<see cref="ConsultantCheckMode.AnswerAsync"/>), git for real, the Kimi shim played (<see cref="PlayedKimi"/>) over a
/// fake person (<see cref="KimiFixture"/>) — no turn is paid for and no real <c>kimi</c> starts. Every junction points into the fixture's own
/// <c>testsToDelete-kimi-</c> folder; every folder the check makes lives under this test's own <c>testsToDelete-</c> folders.
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _person.Dispose();
        _homes.Dispose();
        _temp.Dispose();
        _data.Dispose();
    }

    private string TheOneShotFolder => Path.Combine(_homes, VibeHomes.OneShotFolder);

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

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

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, so the values reach the test's own flow.</summary>
    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi"),
        },
    };

    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    /// <summary>The turn the check plays: the marker word the check planted in the copy's CHECK.md, read while the check is still running, and a canary it could not read.</summary>
    private static ProcessResult TheMarkerRead(int turn, ProcessRequest request)
    {
        var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
    }

    private static string ArgAfter(PlayedKimi.Launch launch, string flag)
    {
        var args = launch.Request.Arguments.ToList();

        return args[args.IndexOf(flag) + 1];
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = StandIn(_person);
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = TheMarkerRead };
        RecordingTheDeleter();

        var (code, said) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be(ConsultCheckStates.Answered, said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var launch = played.Shims.Should().ContainSingle().Subject;
        ArgAfter(launch, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = ArgAfter(launch, "--home");
        home.Should().StartWith(TheOneShotFolder + Path.DirectorySeparatorChar, "D1: under the homes root's coai-vh, never under TEMP");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().MatchRegex("^[0-9a-f]{8}$");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, ConsultCheckScratch.Prefix + "*").Should().BeEmpty("the scratch repository went with the check");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        using var stood = StandIn(_person);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = theTurnFails
                ? (_, _) => new ProcessResult(70, KimiShimScripts.Report(AcpEnd.SessionLost, string.Empty, "lost", tokens: false, session: PlayedKimi.Minted), string.Empty, TimedOut: false)
                : TheMarkerRead,
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        var (_, said) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        said.GetProperty("state").GetString().Should().Be(theTurnFails ? ConsultCheckStates.Failed : ConsultCheckStates.Answered, said.ToString());
        played.Shims.Should().ContainSingle("the turn ran, so there was a home to be gone");
        Directory.EnumerateFileSystemEntries(TheOneShotFolder).Should().BeEmpty("the one-shot home's folder goes when the check ends, whatever ended it");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder is whole: names, bytes and ReadOnly bits");
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = StandIn(_person);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };
        RecordingTheDeleter();

        var running = CheckAsync(Settings(_homes), played, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        var (_, said) = await running;

        said.GetProperty("state").GetString().Should().Be(ConsultCheckStates.Failed, said.ToString());
        said.GetProperty("failureKind").GetString().Should().Be(ConsultFailureKinds.Cancelled);
        Directory.EnumerateFileSystemEntries(TheOneShotFolder).Should().BeEmpty("the home's folder goes when the check ends");
        TheDeleterNeverMetTheJunction();
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = StandIn(_person);
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = TheMarkerRead };
        var before = _person.Hash();
        RecordingTheDeleter();

        var (_, said) = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        said.GetProperty("state").GetString().Should().Be(ConsultCheckStates.Answered, said.ToString());
        var home = ArgAfter(played.Shims.Should().ContainSingle().Subject, "--home");
        home.Should().Contain(Path.Combine("own", "kh-"), "no root: the home stays in the check's scratch, under own");
        Directory.EnumerateDirectories(_temp, ConsultCheckScratch.Prefix + "*").Should().BeEmpty("the scratch is gone after the check");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder is whole");
    }
}
