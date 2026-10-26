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
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check's turn runs in a
/// COPY of the scratch repository, on a ONE-SHOT home (junction and all) under the homes root, and the home and the copy are disposed
/// junction-first whatever ended the check. Only the shim is played (<see cref="PlayedKimi"/>); git runs for real.
/// </summary>
/// <remarks>
/// The check's own invariant (the scratch repository unchanged, the canary untouched) is what <c>answered</c> proves: a check whose consultant
/// changed either one ends <c>failed</c> with the changes named, not <c>answered</c>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-temp-").FullName;
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _homes.Dispose();
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

    /// <summary>Several scopes, given back in reverse (the <c>AKimiConsultationScenarioTests</c> shape).</summary>
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
            [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi"),
        },
    };

    private async Task<(int Code, JsonElement Said, string Err)> CheckAsync(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    /// <summary>The played turn: it reads the marker the check wrote into the copy's <c>CHECK.md</c> and answers with it.</summary>
    private static ProcessResult AnswerFor(ProcessRequest request)
    {
        var checkFile = Directory.EnumerateFiles(request.WorkingDirectory, "CHECK.md", SearchOption.AllDirectories).Single();
        var marker = File.ReadAllText(checkFile).Trim().Split(' ')[^1].TrimEnd('.');

        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 10, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
    }

    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

    private void TheOneShotFolderIsEmpty()
    {
        var folder = Path.Combine(_homes, VibeHomes.OneShotFolder);

        Directory.EnumerateFileSystemEntries(folder).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");
    }

    private void TheScratchIsGone() =>
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var scopes = StandIn();
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswerFor(request) };

        var (code, said, _) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        // `answered` is the proof that the scratch repository and the canary are unchanged: a consultant that changed either one ends `failed`.
        if (said.TryGetProperty("failureKind", out var kind))
        {
            kind.GetString().Should().BeNullOrEmpty(said.ToString());
        }

        var turn = played.Shims.Should().ContainSingle().Subject.Request;
        After(turn.Arguments, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        turn.WorkingDirectory.Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = After(turn.Arguments, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(Path.Combine(_homes, VibeHomes.OneShotFolder), "under the homes root's coai-vh, never under TEMP");

        ThePersonsFolderIsWhole(before);
    }

    [Theory]
    [InlineData("passed")]
    [InlineData("fails")]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(string ending)
    {
        using var scopes = StandIn();
        var before = _person.Hash();
        RecordingTheDeleter();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = ending == "passed"
                ? (_, request) => AnswerFor(request)
                : (_, _) => KimiShimScripts.Ended(70, AcpEnd.SessionLost, "the agent could not load the session"),
        };

        var (_, said, _) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        if (ending == "passed")
        {
            Text(said, "state").Should().Be("answered", said.ToString());
        }
        else
        {
            Text(said, "state").Should().NotBe("answered", said.ToString());
        }

        TheOneShotFolderIsEmpty();
        TheScratchIsGone();
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
        Directory.Exists(_person.Credentials).Should().BeTrue("the person's sign-in is not the check's to delete");
    }

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var scopes = StandIn();
        var before = _person.Hash();
        RecordingTheDeleter();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await cancelled.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        var (_, said, _) = await CheckAsync(Settings(_homes), played, cancelled.Token);

        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled", said.ToString());
        TheOneShotFolderIsEmpty();
        TheScratchIsGone();
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var scopes = StandIn();
        var before = _person.Hash();
        RecordingTheDeleter();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswerFor(request) };

        var (_, said, _) = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        var turn = played.Shims.Should().ContainSingle().Subject.Request;
        var home = After(turn.Arguments, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "the home is <scratch>\\own\\kh-…");
        TheScratchIsGone();
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }
}
