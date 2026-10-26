using System.Text.Json;
using CoaiMcp.Runners.Acp;
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
/// it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home (junction and all) under <c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>,
/// disposed junction-first whatever ended the check; and the check's own invariant (the scratch repository unchanged, the canary untouched) holds.
/// </summary>
/// <remarks>
/// The check mode runs in process (<see cref="ConsultantCheckMode.AnswerAsync"/>), git for real, the Kimi shim played (<see cref="PlayedKimi"/>) over a
/// fake person's sign-in (<see cref="KimiFixture"/>); no turn is paid for and the real <c>kimi</c> never starts. Shaped like the Vibe checks of
/// <see cref="ConsultantCheckTests"/>. The person's folder and the homes root are test folders named <c>testsToDelete-…</c>.
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-temp-").FullName;
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _homes.Dispose();
        _person.Dispose();
        foreach (var dir in (string[])[_data, _temp])
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    /// <summary>The settings of a check of one Kimi row; <paramref name="homesRoot"/> empty is a hand-built, root-less one (S5.5.4).</summary>
    private PanelSettings Settings(string homesRoot, TimeSpan? timeout = null) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = timeout ?? TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") },
    };

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>The value after <paramref name="flag"/> in a shim request's arguments.</summary>
    private static string After(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    /// <summary>A played Kimi whose answer reads the check's marker in the folder the shim stood in — what a consultation's turn does (see <c>AVibeCheck_…</c>).</summary>
    private PlayedKimi Played(Func<ProcessRequest, ProcessResult>? turn = null)
    {
        // QWEN-FILL-BEGIN s55-played test
        return new PlayedKimi(new ProcessLauncher()) { Turn = turn is null ? (_, request) =>
        {
            var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');
            return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
        } : (_, request) => turn(request) };
        // QWEN-FILL-END s55-played
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-check test
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        var played = Played();
        var (code, said, _) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);
        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue();
        var turn = played.Shims.Should().ContainSingle().Subject.Request;
        After(turn, "--cwd").Should().Contain(Path.Combine("own", "tree"));
        var home = After(turn, AcpWords.HomeFlag);
        home.Should().StartWith(Path.Combine(_homes, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(Path.Combine(_homes, VibeHomes.OneShotFolder));
        if (said.TryGetProperty("canaryTouched", out var canaryTouched) && canaryTouched.GetBoolean())
        {
            Assert.Fail("Expected the check of my-kimi to pass, but it failed: the vendor 'my-kimi' runs on 'kimi', which cannot hold a consultation in this build.");
        }
        if (said.TryGetProperty("treeChanged", out var treeChanged) && treeChanged.GetBoolean())
        {
            Assert.Fail("Expected the check of my-kimi to pass, but it failed: the vendor 'my-kimi' runs on 'kimi', which cannot hold a consultation in this build.");
        }
        // QWEN-FILL-END s551-check
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        // QWEN-FILL-BEGIN s552-gone test
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        var played = Played(theTurnFails ? (_, _) => new ProcessResult(70, string.Empty, "the played shim failed" + Environment.NewLine, TimedOut: false) : null);
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        var before = _person.Hash();
        RecordingTheDeleter();
        await Check(Settings(_homes), played, TestContext.Current.CancellationToken);
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        // QWEN-FILL-END s552-gone
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancel test
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        RecordingTheDeleter();
        var before = _person.Hash();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher()) { During = async (_, ct) => { await caller.CancelAsync(); ct.ThrowIfCancellationRequested(); } };
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        var (_, said, _) = await Check(Settings(_homes), played, caller.Token);
        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled");
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty();
        _unlinkedByTheDeleter.Should().BeEmpty();
        _person.Hash().Should().Equal(before);
        // QWEN-FILL-END s553-cancel
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        RecordingTheDeleter();
        var before = _person.Hash();
        var played = Played();
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        var (code, said, _) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);
        code.Should().Be(0);
        var turn = played.Shims.Should().ContainSingle().Subject.Request;
        var home = After(turn, AcpWords.HomeFlag);
        home.Should().Contain(Path.Combine("own", "kh-"));
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch is gone");
        _unlinkedByTheDeleter.Should().BeEmpty();
        _person.Hash().Should().Equal(before);
        // QWEN-FILL-END s554-rootless
    }
}
