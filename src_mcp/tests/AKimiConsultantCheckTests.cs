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
/// <c>--check-consultant</c> with a Kimi row runs one Kimi turn as a consultation does (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>,
/// S5.5.1–S5.5.4): in a COPY of the check's scratch repository, on a ONE-SHOT home under the homes root, disposed junction-first however the
/// check ended — and with no homes root, on a home inside the check's own scratch, disposed junction-first with the scratch.
/// </summary>
/// <remarks>
/// Only the shim is played (<see cref="PlayedKimi"/>), over a fake person (<see cref="KimiFixture"/>) and the real launch control, through
/// <see cref="WindowsStandIn"/> — so these run on every OS and no turn is paid for.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinked = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _temp.Dispose();
        _data.Dispose();
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

    /// <summary>The person, the host and the lock root this check's Kimi adapter sees, for this test's own async flow.</summary>
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

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct, params string[] args)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", .. args], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>The value of a flag in a launch's arguments: the word after it.</summary>
    private static string ArgAfter(IReadOnlyList<string> arguments, string flag)
    {
        var at = arguments.ToList().IndexOf(flag);
        at.Should().BeGreaterThanOrEqualTo(0, $"the launch names {flag}");

        return arguments[at + 1];
    }

    /// <summary>The answer a played Kimi gives: the marker it reads from the COPY's <c>CHECK.md</c>, and the canary it cannot read.</summary>
    private static ProcessResult AnsweredFromTheCopy(ProcessRequest request)
    {
        var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 20, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
    }

    /// <summary>The one-shot home is gone, junction first (the deleter never met it), and the person's folder is whole.</summary>
    private void TheHomeIsGone(IReadOnlyList<string> before)
    {
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home's folder goes when the check ends");
        _unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked: KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    /// <summary>S5.5.1: the check's Kimi turn runs in a copy, on a one-shot home under the homes root, and reads the marker.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var standIn = StandIn(_person);
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnsweredFromTheCopy(request) };

        var (code, said, _) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken, "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var launch = played.Shims.Should().ContainSingle().Subject;
        ArgAfter(launch.Request.Arguments, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = ArgAfter(launch.Request.Arguments, AcpWords.HomeFlag);
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(Path.Combine(_homes, VibeHomes.OneShotFolder), "the one-shot home under the homes root");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
    }

    /// <summary>S5.5.2: however the check ends — answered, or the agent lost its session — the home is gone, junction first.</summary>
    [Fact]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst()
    {
        using var standIn = StandIn(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinked.Add;

        var answered = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnsweredFromTheCopy(request) };
        var (_, said, _) = await Check(Settings(_homes), answered, TestContext.Current.CancellationToken, "--caller", "claude");
        Text(said, "state").Should().Be("answered", said.ToString());
        TheHomeIsGone(before);

        var lost = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) };
        var (_, failed, _) = await Check(Settings(_homes), lost, TestContext.Current.CancellationToken, "--caller", "claude");
        Text(failed, "state").Should().NotBe("answered", failed.ToString());
        TheHomeIsGone(before);
    }

    /// <summary>S5.5.3: a check cancelled mid-turn is recorded as cancelled, and still disposes its one-shot home.</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var standIn = StandIn(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinked.Add;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        var (_, said, _) = await Check(Settings(_homes), played, caller.Token, "--caller", "claude");

        Text(said, "state").Should().NotBe("answered", said.ToString());
        Text(said, "failureKind").Should().Be(ConsultFailureKinds.Cancelled, said.ToString());
        TheHomeIsGone(before);
    }

    /// <summary>S5.5.4: with no homes root, the home is kept inside the check's own scratch, and the scratch's disposal takes it junction first.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var standIn = StandIn(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinked.Add;
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnsweredFromTheCopy(request) };

        var (code, said, _) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken, "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        var home = ArgAfter(played.Shims.Should().ContainSingle().Subject.Request.Arguments, AcpWords.HomeFlag);
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "the home is kept one level down, in the check's scratch");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, ConsultCheckScratch.Prefix + "*").Should().BeEmpty("the scratch goes with the check, home and all");
        _unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked: KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }
}
