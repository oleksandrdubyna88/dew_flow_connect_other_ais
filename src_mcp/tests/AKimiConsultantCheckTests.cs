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
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the real check, the real
/// copy (git runs for real), the real launch control over a fake person's sign-in (<see cref="KimiFixture"/>, a symbolic link standing in for
/// the junction off Windows through <see cref="WindowsStandIn"/>) — only the shim is played (<see cref="PlayedKimi"/>), so no turn is paid for.
/// </summary>
/// <remarks>No new branch in the check (decision 1): it gives a copy to a runtime that asks and a one-shot home to one that declares a homes folder.</remarks>
[Collection("fakecli-env")]
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
        _person.Dispose();
        _homes.Dispose();
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

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see (the AKimiConsultationScenarioTests.StandIn shape).</summary>
    private Scopes StandIn() => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") },
    };

    private async Task<(int Code, JsonElement Said)> Check(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    /// <summary>A turn that reads the marker from the COPY it stands in, as a consultant would, and leaves the canary alone.</summary>
    private static ProcessResult ReadsTheMarker(ProcessRequest request)
    {
        var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
    }

    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private string OneShotRoot => Path.Combine(_homes, VibeHomes.OneShotFolder);

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = StandIn();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => ReadsTheMarker(request) };

        var (code, said) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        var cwd = After(shim.Arguments, "--cwd");
        cwd.Should().StartWith(_temp).And.Contain(Path.Combine(ConsultantCheck.OwnName, "tree"), "a copy in the check's own scratch, never the repository (D6)");
        shim.WorkingDirectory.Should().Be(cwd);
        var home = After(shim.Arguments, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home))!.Should().HaveLength(8);
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(OneShotRoot, "J3: <root>/coai-vh/<8>/kh-<10 hex>");
        shim.Arguments.Should().NotContain(AcpWords.SessionFlag, "a check is one turn");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool answered)
    {
        using var stood = StandIn();
        var before = _person.Hash();
        RecordingTheDeleter();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => answered ? ReadsTheMarker(request) : PlayedKimi.Lost(PlayedKimi.SessionOf(request)),
        };

        var (code, said) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be(answered ? "answered" : "failed", said.ToString());
        played.Shims.Should().ContainSingle("the home was made and the shim ran");
        Directory.EnumerateFileSystemEntries(OneShotRoot).Should().BeEmpty("coai-vh/<8> goes when the check ends, whatever ended it");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
    }

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = StandIn();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        var (code, said) = await Check(Settings(_homes), played, caller.Token);

        code.Should().Be(0);
        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled");
        played.Shims.Should().ContainSingle("the turn had started");
        Directory.EnumerateFileSystemEntries(OneShotRoot).Should().BeEmpty("coai-vh/<8> goes when the check ends, whatever ended it");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
    }

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = StandIn();
        var before = _person.Hash();
        RecordingTheDeleter();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => ReadsTheMarker(request) };

        var (code, said) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        var home = After(played.Shims.Should().ContainSingle().Subject.Request.Arguments, AcpWords.HomeFlag);
        home.Should().StartWith(_temp, "no root: the home stays in the check's own scratch (E5.5 decision 2)");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("ConsultCheckScratch.Delete took the scratch whole");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        Directory.Exists(OneShotRoot).Should().BeFalse("a root-less check reserves nothing under a homes root");
    }
}
