using System.Text.Json;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation makes
/// it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>), disposed
/// junction-first whatever ended the check. There is no new branch in the check (decision 1): these tests prove the existing path with a new adapter.
/// </summary>
/// <remarks>
/// The whole mode in process (<see cref="ConsultantCheckMode.AnswerAsync"/>), git for real, the real launch control over a fake person
/// (<see cref="KimiFixture"/>, stood in per async flow by <see cref="AKimiConsultationScenarioTests.StandIn"/>), only the shim played
/// (<see cref="PlayedKimi"/>) — no turn is paid for and the real <c>kimi</c> is never started. Every folder is a <c>testsToDelete-</c> folder of the test.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-check-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public AKimiConsultantCheckTests() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();
    }

    /// <summary>The homes root of the scenario — inside its own folder, never the machine's local data.</summary>
    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    /// <summary>The check's temp folder: <c>coai-check-*</c> scratch folders are made here.</summary>
    private string Temp => Directory.CreateDirectory(_dir.At("temp")).FullName;

    /// <summary>The panel settings of a check of a Kimi row; <paramref name="homesRoot"/> is empty for the rootless arm (S5.5.4).</summary>
    private PanelSettings Settings(string homesRoot)
    {
        return new PanelSettings
        {
            Providers = [],
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(60),
            ConsultEnabled = true,
            ConsultantHomesRoot = homesRoot,
            Consultants = new Dictionary<string, ConsultantChoice>
            {
                [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi"),
            },
        };
    }

    /// <summary>The played shim: it reads the marker from the copy's CHECK.md and answers <c>marker: …</c> and <c>canary: CANNOT</c>, as the check wants.</summary>
    private static PlayedKimi Played()
    {
        return new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) =>
            {
                var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');
                var answer = $"marker: {marker}{Environment.NewLine}canary: CANNOT";
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, answer);
            },
        };
    }

    /// <summary>Runs the check as the CLI mode does, with <paramref name="launcher"/> in front of the shim: the exit code, the printed JSON, and the cancellation token it was given.</summary>
    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi launcher, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], launcher, Temp, _warned.Add, Noticing.None, ct);
        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        var played = Played();

        var (code, said) = await CheckAsync(Settings(Homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");

        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        var args = shim.Arguments.ToList();

        var cwd = args[args.IndexOf("--cwd") + 1];
        cwd.Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        Path.GetFullPath(cwd).StartsWith(Path.GetFullPath(Temp), StringComparison.OrdinalIgnoreCase).Should().BeTrue("the copy lives in the check's temp folder: {0}", cwd);

        var home = args[args.IndexOf("--home") + 1];
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(Path.Combine(Homes, VibeHomes.OneShotFolder));

        Directory.EnumerateDirectories(Temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData("passed")]
    [InlineData("exit 70")]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(string ending)
    {
        ending.Should().NotBeNullOrEmpty("the arm's name");
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        var before = _person.Hash();
        var played = ending == "passed"
            ? Played()
            : new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) };

        var (_, said) = await CheckAsync(Settings(Homes), played, TestContext.Current.CancellationToken);

        played.Shims.Should().NotBeEmpty("a home was made before the end, so the disposal assertions below can see it go");
        var oneShot = Path.Combine(Homes, VibeHomes.OneShotFolder);
        (Directory.Exists(oneShot) ? Directory.EnumerateFileSystemEntries(oneShot) : Enumerable.Empty<string>())
            .Should().BeEmpty("coai-vh\\<8> is gone; the folder coai-vh itself may exist");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

        if (ending == "passed")
        {
            said.GetProperty("state").GetString().Should().Be("answered");
        }
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        var before = _person.Hash();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        var (code, said) = await CheckAsync(Settings(Homes), played, caller.Token);

        said.ValueKind.Should().Be(JsonValueKind.Object, "the check prints its record when the caller cancels");
        said.GetProperty("state").GetString().Should().Be(ConsultCheckStates.Failed, said.ToString());
        said.GetProperty("failureKind").GetString().Should().Be("cancelled", said.ToString());
        played.Shims.Should().ContainSingle("a home with its junction was made before the cancel");

        var oneShot = Path.Combine(Homes, VibeHomes.OneShotFolder);
        (Directory.Exists(oneShot) ? Directory.EnumerateFileSystemEntries(oneShot) : Enumerable.Empty<string>())
            .Should().BeEmpty("coai-vh\\<8> is gone");
        _unlinkedByTheDeleter.Should().BeEmpty();
        _person.Hash().Should().Equal(before);
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        var before = _person.Hash();
        var played = Played();

        var (_, said) = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());

        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        var args = shim.Arguments;
        var home = args[args.ToList().IndexOf("--home") + 1];
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName);

        Directory.EnumerateDirectories(Temp, "coai-check-*").Should().BeEmpty("the check's scratch goes whole");
        _unlinkedByTheDeleter.Should().BeEmpty("KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder is whole");
    }
}
