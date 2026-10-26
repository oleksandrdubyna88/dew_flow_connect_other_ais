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
/// <c>--check-consultant</c> on a Kimi row, in process (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs
/// in a COPY of its scratch (<c>&lt;scratch&gt;\own\tree</c>), on a one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>), and that home
/// goes junction-first whatever ended the check — passed, exit 70, cancelled, and rootless. Only the shim is played (<see cref="PlayedKimi"/>);
/// the real launch control runs over a fake person (<see cref="KimiFixture"/>).
/// </summary>
/// <remarks>
/// Every case starts with <c>Assert.SkipUnless</c> on Windows (D18): Kimi is refused on every other host, so on Linux these cases SKIP.
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-check-");
    private readonly List<string> _unlinked = [];
    private readonly List<string> _warned = [];
    private readonly string _temp;
    private readonly string _data;

    public AKimiConsultantCheckTests()
    {
        GitScratch.AfterUnlink = _unlinked.Add;
        _temp = _dir.At("temp");
        _data = _dir.At("data");
        Directory.CreateDirectory(_temp);
        Directory.CreateDirectory(_data);
    }

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _person.Dispose();
        _dir.Dispose();
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
    /// The person, the host and the lock root this check's Kimi adapters see — set by a SYNCHRONOUS call, so the values reach the test's own flow.
    /// The caller is named by <c>--caller claude</c>, so no caller variable is stood in here.
    /// </summary>
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

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string ArgOf(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    /// <summary>The marker the check's copy holds: its last word, read from <c>CHECK.md</c> in the copy the shim is run in.</summary>
    private static string MarkerAnswer(ProcessRequest request)
    {
        var seen = File.ReadAllText(Path.Combine(ArgOf(request, "--cwd"), "CHECK.md"));

        return $"marker: {seen.Trim().Split(' ')[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT";
    }

    /// <summary>A shim that reads the marker in its copy and answers it.</summary>
    private static PlayedKimi Marked() => new(new ProcessLauncher())
    {
        Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 10, 1, MarkerAnswer(request)),
    };

    /// <summary>What is left in the one-shot folder under the homes root — an absent folder is empty.</summary>
    private static List<string> OneShotEntries(string homes)
    {
        var folder = Path.Combine(homes, VibeHomes.OneShotFolder);

        return Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder).ToList() : [];
    }

    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        using var homes = TempDir.For("testsToDelete-kimi-vh-");
        using var scope = StandIn(_person);
        var played = Marked();

        var (code, said, _) = await Check(Settings(homes.Path), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var launch = played.Shims.Should().ContainSingle().Subject;
        ArgOf(launch.Request, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = ArgOf(launch.Request, "--home");
        home.Should().StartWith(Path.Combine(homes.Path, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar, "a one-shot home under the short homes root");
        Path.GetFileName(home).Should().StartWith(KimiHome.Prefix, "the home is a Kimi home of its own");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData("passed")]
    [InlineData("exit70")]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(string ending)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        using var homes = TempDir.For("testsToDelete-kimi-vh-");
        using var scope = StandIn(_person);
        var played = ending == "passed"
            ? Marked()
            : new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) };
        var before = _person.Hash();

        await Check(Settings(homes.Path), played, TestContext.Current.CancellationToken);

        OneShotEntries(homes.Path).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");
        _unlinked.Should().BeEmpty("the deleter never met the junction");
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        using var homes = TempDir.For("testsToDelete-kimi-vh-");
        using var scope = StandIn(_person);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        var (_, said, _) = await Check(Settings(homes.Path), played, caller.Token);

        said.GetProperty("state").GetString().Should().Be("failed", said.ToString());
        said.GetProperty("failureKind").GetString().Should().Be(ConsultFailureKinds.Cancelled);
        OneShotEntries(homes.Path).Should().BeEmpty("a cancelled check disposes its home too");
        _unlinked.Should().BeEmpty("the deleter never met the junction");
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        using var scope = StandIn(_person);
        var played = Marked();

        await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        var home = ArgOf(played.Shims.Should().ContainSingle().Subject.Request, "--home");
        home.Should().Contain(Path.DirectorySeparatorChar + ConsultantCheck.OwnName + Path.DirectorySeparatorChar + KimiHome.Prefix,
            "with no homes root the home sits in the check's own scratch, one level down");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch, and the home in it, went with the check");
        _unlinked.Should().BeEmpty("the deleter never met the junction");
    }
}
