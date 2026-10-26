using System.Text.Json;
using System.Text.RegularExpressions;
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
/// <c>--check-consultant</c> of a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4, decisions 1–3): the turn runs in a
/// COPY of the check's scratch, on a one-shot home that is disposed junction-first on every ending; a rootless check keeps its home in its own scratch.
/// </summary>
/// <remarks>
/// Shaped like the Vibe check in <see cref="ConsultantCheckTests"/>, whose helpers are copied here, not shared. The Kimi sign-in is a fake person
/// (<see cref="KimiFixture"/>) behind the real launch control, stood in for Windows; only the shim is played (<see cref="PlayedKimi"/>), so no turn
/// is paid for and the real <c>kimi</c> is never started.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public AKimiConsultantCheckTests() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _homes.Dispose();
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

    /// <summary>The stand-in every Kimi check runs in: Windows, the fake person's sign-in and its lock root (as <c>AKimiConsultationScenarioTests.StandIn</c>).</summary>
    private Scopes StandIn() => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("my-kimi", Model: string.Empty, Runtime: "kimi", ExecutablePath: PlayedKimi.Shim),
        },
    };

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct, params string[] args)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", .. args], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>The marker the copy's <c>CHECK.md</c> holds — its last word, read as the Vibe check reads it.</summary>
    private static string MarkerIn(ProcessRequest request) =>
        File.ReadAllText(Path.Combine(KimiEnvironment.FlagOf(request, "--cwd"), "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

    /// <summary>An answered turn that reads the marker and says the canary was not attempted.</summary>
    private static ProcessResult AnsweredWithTheMarker(ProcessRequest request) =>
        PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 10, $"marker: {MarkerIn(request)}\ncanary: CANNOT");

    private string OneShotFolder => Path.Combine(_homes, VibeHomes.OneShotFolder);

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var standIn = StandIn();
        var kimi = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnsweredWithTheMarker(request) };

        var (code, said, _) = await Check(Settings(_homes), kimi, TestContext.Current.CancellationToken, "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        var launch = kimi.Shims.Should().ContainSingle().Subject.Request;
        KimiEnvironment.FlagOf(launch, "--cwd").Should().Contain(Path.Combine(ConsultantCheck.OwnName, "tree"), "a copy in the check's own scratch, never the scratch itself");
        var separator = Regex.Escape(Path.DirectorySeparatorChar.ToString());
        KimiEnvironment.FlagOf(launch, AcpWords.HomeFlag).Should().MatchRegex(
            "^" + Regex.Escape(_homes.Path) + separator + Regex.Escape(VibeHomes.OneShotFolder) + separator + "[0-9a-f]{8}" + separator + "kh-[0-9a-f]{10}$",
            "the home is short and one-shot, under the homes root");
        Text(said, "confinement").Should().BeEmpty("decision 3: Kimi's confinement does not depend on its CLI's version");
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        Text(said, "canary").Should().Be("not-attempted");
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("failed")]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(string ending)
    {
        using var standIn = StandIn();
        var kimi = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => ending == "answered" ? AnsweredWithTheMarker(request) : PlayedKimi.Lost(PlayedKimi.SessionOf(request)),
        };
        var before = _person.Hash();
        var credentials = Path.Combine(_person.Credentials, "kimi-code-env-x.json");

        var (_, said, _) = await Check(Settings(_homes), kimi, TestContext.Current.CancellationToken, "--caller", "claude");

        Text(said, "state").Should().Be(ending, said.ToString());
        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");
        _unlinkedByTheDeleter.Should().BeEmpty("the junction is removed first, never deleted through");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        File.GetAttributes(credentials).HasFlag(FileAttributes.ReadOnly).Should().BeTrue("the person's credentials were read-only, and still are");
    }

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var standIn = StandIn();
        using var cts = new CancellationTokenSource();
        var kimi = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, token) =>
            {
                await cts.CancelAsync();
                await Task.Delay(Timeout.Infinite, token);
            },
        };

        var (_, said, _) = await Check(Settings(_homes), kimi, cts.Token, "--caller", "claude");

        Text(said, "state").Should().NotBe("answered", said.ToString());
        Text(said, "failureKind").Should().Be(ConsultFailureKinds.Cancelled);
        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("a cancelled check disposes its home too");
        _unlinkedByTheDeleter.Should().BeEmpty("the junction is removed first, never deleted through");
    }

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var standIn = StandIn();
        var kimi = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnsweredWithTheMarker(request) };

        var (_, said, _) = await Check(Settings(string.Empty), kimi, TestContext.Current.CancellationToken, "--caller", "claude");

        Text(said, "state").Should().Be("answered", said.ToString());
        var launch = kimi.Shims.Should().ContainSingle().Subject.Request;
        KimiEnvironment.FlagOf(launch, AcpWords.HomeFlag).Should().Contain(Path.Combine(ConsultantCheck.OwnName, "kh-"), "with no root, the home is one level down from the scratch root");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch goes whole");
        _unlinkedByTheDeleter.Should().BeEmpty("the junction is removed first, never deleted through");
    }
}
