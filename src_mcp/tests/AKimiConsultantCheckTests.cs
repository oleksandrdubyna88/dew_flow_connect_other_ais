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
/// E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c> (S5.5.1–S5.5.4): <c>coai-mcp --check-consultant</c> on a Kimi row runs one turn as a
/// consultation makes it — in a COPY of the check's scratch repository, on a one-shot home it owns, disposed junction-first however the check ended.
/// Only the shim is played (<see cref="PlayedKimi"/>), so no turn is paid for and the real <c>kimi</c> is never started; the person's sign-in is a
/// fake one (<see cref="KimiFixture"/>) and the host is stood in as Windows (<see cref="WindowsStandIn"/>).
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-checktest-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("coai-checktest-temp-").FullName;
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-");

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
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

    /// <summary>A Kimi consultant row; <paramref name="homesRoot"/> empty is the settings built by hand (no root).</summary>
    private PanelSettings OnKimi(string homesRoot) => new()
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

    /// <summary>The person, the host and the lock root the check's Kimi adapter sees — the stand-in of AKimiConsultationScenarioTests, without a caller.</summary>
    private IDisposable StandIn() => new Scopes(
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

    /// <summary>The played shim answering the check: the marker read from the copy it stands in, and the canary it cannot read.</summary>
    private static PlayedKimi Answering() => new(new ProcessLauncher())
    {
        Turn = static (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 1200, 40,
            $"marker: {File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT"),
    };

    private async Task<(int Code, JsonElement Said)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string HomeOf(PlayedKimi played) =>
        After(played.Shims.Should().ContainSingle().Subject.Request.Arguments, AcpWords.HomeFlag);

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    private string OneShotRoot => Path.Combine(_homes.Path, VibeHomes.OneShotFolder);

    private void TheHomesAreGone() =>
        (!Directory.Exists(OneShotRoot) || !Directory.EnumerateFileSystemEntries(OneShotRoot).Any()).Should().BeTrue("coai-vh/<8> goes when the check ends");

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    /// <summary>
    /// S5.5.1 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>: the check's Kimi turn runs in a copy of the scratch repository, on a one-shot home
    /// under the homes root's <c>coai-vh</c>, never under TEMP, and reads its marker from the copy.
    /// </summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var standIn = StandIn();
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var played = Answering();

        var (code, said) = await Check(OnKimi(_homes.Path), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        shim.WorkingDirectory.Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        shim.Arguments.Should().ContainInConsecutiveOrder("--cwd", shim.WorkingDirectory);
        var home = HomeOf(played);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)!).Should().HaveLength(8);
        Path.GetDirectoryName(Path.GetDirectoryName(home)!).Should().Be(OneShotRoot, "J3: under the homes root's coai-vh, never TEMP");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }

    /// <summary>
    /// S5.5.2 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>: after the check, answered or lost, the one-shot home is gone — its junction is
    /// unlinked first, so the deleter never meets it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool answers)
    {
        using var standIn = StandIn();
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var played = answers
            ? Answering()
            : new PlayedKimi(new ProcessLauncher()) { Turn = static (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) };

        var (code, said) = await Check(OnKimi(_homes.Path), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be(answers ? "answered" : "failed", said.ToString());
        played.Shims.Should().ContainSingle();
        TheHomesAreGone();
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }

    /// <summary>
    /// S5.5.3 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>: a check cancelled while its turn runs still disposes its one-shot home, junction first.
    /// </summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var standIn = StandIn();
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        using var cancelling = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = Answering().Turn,
            During = async (_, ct) =>
            {
                await cancelling.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        var (code, said) = await Check(OnKimi(_homes.Path), played, cancelling.Token);

        code.Should().Be(0);
        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled");
        played.Shims.Should().ContainSingle("PlayedKimi keeps a launch in its finally, so a cancelled one is kept too");
        TheHomesAreGone();
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }

    /// <summary>
    /// S5.5.4 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>: with no homes root, the one-shot home is kept in the check's own scratch
    /// (<c>own/kh-*</c>) and disposed junction first when the check ends.
    /// </summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var standIn = StandIn();
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var played = Answering();

        var (code, said) = await Check(OnKimi(string.Empty), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        var home = HomeOf(played);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)!).Should().Be(ConsultantCheck.OwnName);
        home.Should().StartWith(_temp, "the check's own scratch");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch, the copy and the home are gone");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }
}
