using System.Text.Json;
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
/// <c>--check-consultant</c> on Kimi (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs ONE Kimi turn the way a
/// consultation makes it — in a COPY of the check's scratch repository, on a one-shot home (<c>coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>, or under the
/// check's own scratch when there is no homes root), disposed junction-first whatever ended the check.
/// </summary>
/// <remarks>
/// The real check, the real git, the real launch control over a fake person (<see cref="KimiFixture"/>) and the real one-shot disposal; only the
/// shim is played (<see cref="PlayedKimi"/>), so no turn is paid for and the real <c>kimi</c> is never started. Every folder a test makes is the
/// fixture's own, the check's temp root, the data directory or a homes folder under a <c>testsToDelete-</c> name.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _temp = TempDir.For("testsToDelete-kcheck-temp-");
    private readonly TempDir _data = TempDir.For("testsToDelete-kcheck-data-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];
    private IReadOnlyList<string> _before = [];

    public AKimiConsultantCheckTests() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _temp.Dispose();
        _data.Dispose();
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

    /// <summary>The person, the host and the lock root the Kimi adapters see — set by a SYNCHRONOUS call, so they reach the test's own flow.</summary>
    private static IDisposable StandIn(KimiFixture person) => new Scopes(
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
        Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") },
    };

    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>A played turn that gives the check what it asks: the marker read from the COPY's <c>CHECK.md</c>, and the canary unread.</summary>
    private static PlayedKimi Marking(IProcessLauncher inner) => new(inner)
    {
        Turn = (_, request) =>
        {
            var cwd = request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];
            var marker = File.ReadAllText(Path.Combine(cwd, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

            return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 1000, 20, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
        },
    };

    /// <summary>The home a shim was launched with (<c>--home</c>).</summary>
    private static string HomeOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--home") + 1];

    /// <summary>The copy a shim was launched in (<c>--cwd</c>).</summary>
    private static string CwdOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];

    /// <summary>The home is gone, and the deleter never met its junction; the person's folder is as it was.</summary>
    private void TheHomeIsGone()
    {
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(_before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = StandIn(_person);
        _before = _person.Hash();
        using var homes = TempDir.For("testsToDelete-kcheck-homes-");
        var played = Marking(new ProcessLauncher());

        var (code, said) = await CheckAsync(Settings(homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");

        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        CwdOf(shim).Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = HomeOf(shim);
        home.Should().StartWith(Path.Combine(homes, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar, "under the short homes root (D1), never under TEMP");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)!).Should().MatchRegex("^[0-9a-f]{8}$");

        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy and the scratch go with the check");
    }

    [Fact]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst()
    {
        using var stood = StandIn(_person);
        _before = _person.Hash();

        // Arm A: the check passes.
        using (var homes = TempDir.For("testsToDelete-kcheck-homes-"))
        {
            var played = Marking(new ProcessLauncher());

            var (code, said) = await CheckAsync(Settings(homes), played, TestContext.Current.CancellationToken);

            code.Should().Be(0);
            Text(said, "state").Should().Be("answered", said.ToString());
            Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the one-shot folder goes when the check ends");
            TheHomeIsGone();
        }

        // Arm B: the turn fails with exit 70.
        using (var homes = TempDir.For("testsToDelete-kcheck-homes-"))
        {
            var failing = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) };

            var (_, said) = await CheckAsync(Settings(homes), failing, TestContext.Current.CancellationToken);

            Text(said, "state").Should().NotBe("answered", said.ToString());
            Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the one-shot folder goes when the check ends");
            TheHomeIsGone();
        }
    }

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = StandIn(_person);
        _before = _person.Hash();
        using var homes = TempDir.For("testsToDelete-kcheck-homes-");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        var (_, said) = await CheckAsync(Settings(homes), played, caller.Token);

        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        Directory.Exists(Path.GetDirectoryName(HomeOf(shim))).Should().BeFalse("the home's folder goes with the cancelled check");
        Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty();
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty();
        TheHomeIsGone();
    }

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = StandIn(_person);
        _before = _person.Hash();
        var played = Marking(new ProcessLauncher());

        var (_, said) = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        // The scratch is gone after the check, so the home is read from the launch the played shim kept.
        var seenHome = HomeOf(played.Shims.Should().ContainSingle().Subject.Request);
        Path.GetFileName(Path.GetDirectoryName(seenHome)).Should().Be("own");
        Path.GetFileName(seenHome).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the whole scratch, home included, is gone");
        TheHomeIsGone();
    }
}
