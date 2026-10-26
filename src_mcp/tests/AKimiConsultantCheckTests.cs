using System.Text.Json;
using System.Text.RegularExpressions;
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
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs one turn as a consultation
/// makes it — in a COPY of its scratch repository, on a ONE-SHOT owned home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>), disposed junction-first
/// whatever ended it. Nothing new in the check itself (decision 1): this proves the path <c>KimiConsultant</c> opens (<c>NeedsASanitisedSnapshot</c>,
/// <c>HomesFolder</c>).
/// </summary>
/// <remarks>
/// The check's own code runs for real (<c>ConsultantCheckMode.AnswerAsync</c>, git for real); the shim is played (<see cref="PlayedKimi"/>) over a
/// fake person (<see cref="KimiFixture"/>) — no turn is paid for and the real <c>kimi</c> is never started. Every folder is a
/// <c>testsToDelete-</c> folder of this test. In the <c>fakecli-env</c> collection because <c>GitScratch.AfterUnlink</c> is a static recorder.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _temp.Dispose();
        _data.Dispose();
        _homes.Dispose();
        _person.Dispose();
    }

    /// <summary>Settings for the caller <c>claude</c> consulting <c>my-kimi</c> (runtime <c>kimi</c>); <paramref name="homesRoot"/> empty is the hand-built, root-less case.</summary>
    private PanelSettings Settings(string homesRoot)
    {
        return new PanelSettings
        {
            Providers = [],
            DataDir = _data.Path,
            ReviewerTimeout = TimeSpan.FromSeconds(60),
            ConsultEnabled = true,
            ConsultantHomesRoot = homesRoot,
            Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") },
        };
    }

    /// <summary>Runs <c>--check-consultant --caller claude</c> through <c>ConsultantCheckMode.AnswerAsync</c> with <paramref name="played"/> as the launcher.</summary>
    private async Task<(int Code, JsonElement Said, string Err)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp.Path, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    /// <summary>A played Kimi whose answer reads the marker out of the CHECK.md of the cwd it was given — the check's own question — and says it cannot read the canary.</summary>
    private static PlayedKimi PlayedThatReadsTheMarker(IProcessLauncher inner, int exit = 0)
    {
        return new PlayedKimi(inner)
        {
            Turn = (_, request) =>
            {
                if (exit != 0)
                {
                    return new ProcessResult(exit, KimiShimScripts.Report(AcpEnd.AgentFailed, string.Empty, "the agent failed", tokens: false), "the agent failed", TimedOut: false);
                }

                var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 10, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
            },
        };
    }

    /// <summary>The shim's argument after <paramref name="flag"/> (<c>--cwd</c>, <c>--home</c>) of the one launch the played Kimi saw.</summary>
    private static string ArgumentAfter(PlayedKimi played, string flag)
    {
        var arguments = played.Shims.Should().ContainSingle().Subject.Request.Arguments.ToList();

        return arguments[arguments.IndexOf(flag) + 1];
    }

    /// <summary>S5.5.1: the check passes in a copy, on a one-shot home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>, and leaves the scratch repository and its canary as they were.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = KimiStandIn.Of(_person);
        var played = PlayedThatReadsTheMarker(new ProcessLauncher());

        var (code, said, _) = await CheckAsync(Settings(_homes.Path), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        said.GetProperty("canary").GetString().Should().Be("not-attempted");
        ArgumentAfter(played, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        ArgumentAfter(played, "--home").Should().MatchRegex($"^{Regex.Escape(Path.Combine(_homes.Path, VibeHomes.OneShotFolder))}[/\\\\][0-9a-f]{{8}}[/\\\\]kh-[0-9a-f]{{10}}$");
        Directory.EnumerateDirectories(_temp.Path, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
    }

    /// <summary>S5.5.2: the check ends (passed; and a second arm where the turn fails exit 70) — the one-shot folder is gone, the deleter met no link, the person's folder is whole.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(70)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(int turnExit)
    {
        using var stood = KimiStandIn.Of(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var played = PlayedThatReadsTheMarker(new ProcessLauncher(), turnExit);

        var (_, said, _) = await CheckAsync(Settings(_homes.Path), played, TestContext.Current.CancellationToken);

        said.GetProperty("state").GetString().Should().Be(turnExit == 0 ? "answered" : "failed", said.ToString());
        var oneShot = Path.GetDirectoryName(ArgumentAfter(played, "--home"))!;
        oneShot.Should().StartWith(Path.Combine(_homes.Path, VibeHomes.OneShotFolder));
        Directory.Exists(oneShot).Should().BeFalse("the check's one-shot folder goes when the check ends, whatever ended it");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    /// <summary>S5.5.3: the played shim blocks, the token is cancelled — the record says cancelled, and the home is gone junction-first (pins the check's <c>finally</c>).</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = KimiStandIn.Of(_person);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await cancelled.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        var (_, said, _) = await CheckAsync(Settings(_homes.Path), played, cancelled.Token);

        said.GetProperty("failureKind").GetString().Should().Be("cancelled", said.ToString());
        var oneShot = Path.GetDirectoryName(ArgumentAfter(played, "--home"))!;
        Directory.Exists(oneShot).Should().BeFalse("a cancelled check gives its one-shot folder back");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    /// <summary>S5.5.4: settings built by hand have no homes root — the home stays in the check's scratch (<c>&lt;scratch&gt;\own\kh-…</c>) and goes with it, junction-first.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = KimiStandIn.Of(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var played = PlayedThatReadsTheMarker(new ProcessLauncher());

        var (_, said, _) = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        var home = ArgumentAfter(played, "--home");
        home.Should().Contain(Path.Combine("own", "kh-"), "with no homes root the home stays in the check's own scratch");
        Directory.EnumerateDirectories(_temp.Path, "coai-check-*").Should().BeEmpty("the scratch goes with the home inside it");
        Directory.Exists(home).Should().BeFalse();
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }
}
