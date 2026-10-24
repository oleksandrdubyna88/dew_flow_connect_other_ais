using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Runners.Acp;
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
/// <c>coai-mcp --check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn made as a
/// consultation makes it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home (junction and all) under
/// <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c> — and that home disposed junction-first whatever ended the check: an answer, a failed turn, a
/// cancellation; with no homes root (settings built by hand), the home lives at <c>&lt;scratch&gt;\own\kh-*</c> and the scratch's own removal
/// disposes it junction-first (E5.3 decision g, judgement R6). Decision 1: no new branch in the check — this proves the path.
/// </summary>
/// <remarks>
/// <para>The real check (<see cref="ConsultantCheckMode.AnswerAsync"/>): the real scratch repository and its canary (git runs for real), the real
/// copy, the real launch control over a fake person's sign-in (<see cref="KimiFixture"/>: a real junction on Windows, a symbolic link elsewhere
/// through <see cref="WindowsStandIn"/>) and a real gate file. Only the shim and <c>kimi --version</c> are played (<see cref="PlayedKimi"/>): the
/// real <c>kimi</c> is never started and no turn is paid for.</para>
/// <para>The check builds its Kimi adapter BY NAME, so the fake person and the measured host are stood in per async flow
/// (<see cref="KimiSeams.Using"/>). The person's folder, the homes root and the check's temp root are three separate <c>testsToDelete-</c> folders:
/// the person's lives OUTSIDE every tree a disposal deletes.</para>
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-check-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    /// <summary>Where the check makes its <c>coai-check-*</c> scratch — this test's own folder, so "gone" is asked of nothing another test makes.</summary>
    private string Temp => Directory.CreateDirectory(_dir.At("temp")).FullName;

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _homes.Dispose();
        _person.Dispose();
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

    /// <summary>The fake person, the measured host and this test's lock root, for this flow's Kimi adapters — set by a SYNCHRONOUS call.</summary>
    private Scopes StandIn() => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private PanelSettings Settings(string homesRoot) => new()
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

    private async Task<JsonElement> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (_, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, Temp, _warned.Add, Noticing.None, ct);

        return JsonDocument.Parse(stdout).RootElement.Clone();
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string After(ProcessRequest shim, string flag) => shim.Arguments[shim.Arguments.ToList().IndexOf(flag) + 1];

    /// <summary>Whether the home a launch stands on held its junction at that moment — there was one for the disposal to unlink.</summary>
    private static bool JunctionIn(ProcessRequest shim) => OwnedPaths.IsLinkIfThere(Path.Combine(After(shim, AcpWords.HomeFlag), "home", "credentials"));

    /// <summary>The marker the check's copy of <c>CHECK.md</c> holds, read where the shim stands — what a consultant that read it answers.</summary>
    private static string MarkerAt(ProcessRequest shim) =>
        File.ReadAllText(Path.Combine(shim.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

    /// <summary>A Kimi that read the marker in the copy it stands in, and cannot read the canary.</summary>
    private static PlayedKimi Reading(ConcurrentQueue<bool> junctions) => new(new ProcessLauncher())
    {
        Turn = (_, shim) => PlayedKimi.Answered(PlayedKimi.SessionOf(shim), 19973, 31, $"marker: {MarkerAt(shim)}{Environment.NewLine}canary: CANNOT"),
        During = (shim, _) =>
        {
            junctions.Enqueue(JunctionIn(shim));

            return Task.CompletedTask;
        },
    };

    private void TheCheckLeftNothing(IReadOnlyList<string> personBefore)
    {
        Directory.EnumerateDirectories(Temp, "coai-check-*").Should().BeEmpty("the check's scratch, its copy with it, goes when the check ends");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(personBefore, "the person's folder: the same names, bytes and read-only bits as before");
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = StandIn();
        var junctions = new ConcurrentQueue<bool>();
        var played = Reading(junctions);

        var said = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", $"the check of my-kimi passes: {said}");
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        Text(said, "confinement").Should().BeEmpty("Kimi's confinement does not depend on its CLI's version (E5.5 decision 3)");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        After(shim, "--cwd").Should().Be(shim.WorkingDirectory, "the shim's read boundary is where it stands");
        Path.GetDirectoryName(shim.WorkingDirectory).Should().EndWith(Path.Combine(ConsultantCheck.OwnName, "tree"), "a copy in the check's own scratch, never the repository (D6)");
        var home = After(shim, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(Path.Combine(_homes, VibeHomes.OneShotFolder), "a one-shot home under <root>\\coai-vh\\<8> (J3)");
        shim.Arguments.Should().NotContain(AcpWords.SessionFlag, "a check is one turn");
        junctions.Should().ContainSingle().Which.Should().BeTrue("the turn stood on a made home, its junction in place");
    }

    // ---------- S5.5.2 ----------

    /// <summary>The check's two outcomes that reach the shim — an answer, and a turn that failed in the agent's own words (exit 70).</summary>
    public static TheoryData<bool> Answers() => [true, false];

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool answers)
    {
        using var stood = StandIn();
        var junctions = new ConcurrentQueue<bool>();
        var reading = Reading(junctions);
        var played = answers ? reading : new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, shim) => new ProcessResult(70, KimiShimScripts.Report(AcpEnd.AgentFailed, string.Empty, "the agent refused", tokens: false, session: PlayedKimi.SessionOf(shim)),
                "the agent refused\r\n", TimedOut: false),
            During = reading.During,
        };
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        var said = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(answers ? "answered" : "failed", said.ToString());
        junctions.Should().ContainSingle().Which.Should().BeTrue("the home's junction was there for the disposal to unlink");
        var oneShot = Path.GetDirectoryName(After(played.Shims.Single().Request, AcpWords.HomeFlag))!;
        Directory.Exists(oneShot).Should().BeFalse($"the check's one-shot folder {oneShot} goes when the check ends");
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty();
        TheCheckLeftNothing(before);
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = StandIn();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var junctions = new ConcurrentQueue<bool>();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (shim, ct) =>
            {
                junctions.Enqueue(JunctionIn(shim));
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        var said = await CheckAsync(Settings(_homes), played, caller.Token);

        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled", "the caller withdrew the check mid-turn");
        junctions.Should().ContainSingle().Which.Should().BeTrue("the turn was running on a made home");
        var oneShot = Path.GetDirectoryName(After(played.Shims.Single().Request, AcpWords.HomeFlag))!;
        Directory.Exists(oneShot).Should().BeFalse("the home goes in finally, whatever ended the check");
        TheCheckLeftNothing(before);
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = StandIn();
        var junctions = new ConcurrentQueue<bool>();
        var played = Reading(junctions);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        var said = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        var home = After(shim, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "no homes root: the home stays at <scratch>\\own\\kh-* (E5.5 decision 2)");
        Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(home))).Should().StartWith("coai-check-");
        junctions.Should().ContainSingle().Which.Should().BeTrue("the home's junction was there for the scratch's removal to unlink");
        Directory.EnumerateFileSystemEntries(_homes).Should().BeEmpty("nothing is written under a root the settings do not name");
        TheCheckLeftNothing(before);
    }
}
