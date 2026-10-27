using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one paid-shaped turn in a COPY
/// of the check's scratch repository, on a ONE-SHOT owned home under <c>coai-vh\&lt;8&gt;</c>, disposed junction-first whatever ended the check.
/// Only the shim is played (<see cref="PlayedKimi"/>), over a fake person (<see cref="KimiFixture"/>) and the measured host
/// (<see cref="WindowsStandIn"/>, <see cref="KimiSeams"/>), so no turn is paid for and the real <c>kimi</c> never starts.
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private const string Advice = PlayedKimi.Advice;

    private readonly KimiFixture _person = new();

    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-data-").FullName;

    private readonly string _temp = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-temp-").FullName;

    private readonly List<string> _warned = [];

    private readonly List<string> _unlinked = [];

    private static readonly IReadOnlyDictionary<string, ConsultantChoice> OnKimi = new Dictionary<string, ConsultantChoice>
    {
        [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi"),
    };

    private string Homes => Directory.CreateDirectory(Path.Combine(_data, "homes")).FullName;

    /// <summary>The fake person, the measured host and the lock root this check's Kimi adapter sees (the consult tests' stand-in).</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _person.Dispose();
        foreach (var dir in (string[])[_data, _temp])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = OnKimi,
    };

    private Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct) =>
        Check(settings, launcher, ct, "--caller", "claude");

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct, params string[] args)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", .. args], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>An answer that reads the check's marker from the copy the shim was given, and says the canary cannot be read.</summary>
    private static ProcessResult AnswerReadingTheMarker(ProcessRequest request)
    {
        var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, answer: $"marker: {marker}{Environment.NewLine}canary: CANNOT");
    }

    private static string ArgumentOf(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    private int OneShotHomesLeft(string homes) =>
        Directory.Exists(Path.Combine(homes, Runners.Vibe.VibeHomes.OneShotFolder))
            ? Directory.EnumerateFileSystemEntries(Path.Combine(homes, Runners.Vibe.VibeHomes.OneShotFolder)).Count()
            : 0;

    /// <summary>S5.5.1: the check passes; the shim's <c>--cwd</c> is a copy in the check's own scratch, and its <c>--home</c> is a one-shot home under the homes root.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stand = StandIn();
        using var homes = TempDir.For("testsToDelete-kimi-check-homes-");
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswerReadingTheMarker(request) };
        var before = _person.Hash();

        var (code, said, _) = await Check(Settings(homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        Text(said, "canary").Should().NotBe("read", said.ToString());
        Text(said, "confinement").Should().BeEmpty("decision 3: the check puts no confinement on the consultant");
        var turn = played.Shims.Should().ContainSingle().Subject.Request;
        ArgumentOf(turn, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = ArgumentOf(turn, "--home");
        home.Should().StartWith(Path.Combine(homes, Runners.Vibe.VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar, "the one-shot home lives under the short homes root");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        _person.Hash().Should().Equal(before, "the fake person's folder is untouched by the check");
    }

    /// <summary>S5.5.2: the one-shot home is gone when the check ends — passed, or refused by an exit 70 — junction-first: the unlink recorder is never invoked, and the person's folder is intact.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool lost)
    {
        using var stand = StandIn();
        using var homes = TempDir.For("testsToDelete-kimi-check-gone-");
        GitScratch.AfterUnlink = path => _unlinked.Add(path);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => lost ? PlayedKimi.Lost(PlayedKimi.SessionOf(request)) : AnswerReadingTheMarker(request),
        };
        var before = _person.Hash();

        var (_, said, _) = await Check(Settings(homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(lost ? "failed" : "answered", said.ToString());
        OneShotHomesLeft(homes).Should().Be(0, "the home's folder goes when the check ends, whatever ended it");
        _unlinked.Should().BeEmpty("the home is disposed junction-first: no recorded unlink ever reaches the person's files");
        _person.Hash().Should().Equal(before, "the fake person's folder is intact, bytes and attributes");
    }

    /// <summary>S5.5.3: a check cancelled while its shim is blocked still disposes its home, junction-first.</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stand = StandIn();
        using var homes = TempDir.For("testsToDelete-kimi-check-cancel-");
        using var cancel = new CancellationTokenSource();
        GitScratch.AfterUnlink = path => _unlinked.Add(path);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                cancel.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };

        var (_, said, _) = await Check(Settings(homes), played, cancel.Token);

        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled", said.ToString());
        OneShotHomesLeft(homes).Should().Be(0, "the check's finally disposes its home whatever ended it");
        _unlinked.Should().BeEmpty("the home is disposed junction-first, even when the check was cancelled");
    }

    /// <summary>S5.5.4: settings built by hand with no homes root keep the home in the check's scratch, and it goes junction-first with the scratch.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stand = StandIn();
        GitScratch.AfterUnlink = path => _unlinked.Add(path);
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswerReadingTheMarker(request) };

        var (_, said, _) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        var home = ArgumentOf(played.Shims.Should().ContainSingle().Subject.Request, "--home");
        home.Should().Contain(Path.Combine("own", "kh-"), "with no root the home sits in the check's own scratch");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch goes when the check ends");
        _unlinked.Should().BeEmpty("the scratch's home is disposed junction-first");
    }

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
}
