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
/// A Kimi consultant check (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>) runs a turn as a consultation does: in a COPY of the check's
/// scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;/coai-vh/&lt;8&gt;/kh-&lt;10 hex&gt;</c>, disposed junction-first whatever ended the check.
/// Only the shim is played (<see cref="PlayedKimi"/>) over a fake person (<see cref="KimiFixture"/>); <c>kimi</c> is never started.
/// </summary>
/// <remarks>
/// The check has no Kimi branch of its own (decision 1): it gives a copy to a runtime that asks for one and a one-shot home to a runtime that names a
/// homes folder, which <c>KimiConsultant</c> does — so these scenarios prove the path, as the plan's S5 bullet asks.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-kimicheck-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("testsToDelete-kimicheck-temp-").FullName;
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimicheck-homes-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinked = [];

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

    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>A Kimi consultant row; <paramref name="homesRoot"/> empty is a check built by hand, with no homes root.</summary>
    private PanelSettings KimiSettings(string homesRoot) => new()
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

    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _ => { }, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    /// <summary>The played agent reads the marker out of the folder it stands in — the check's copy of its scratch — and answers with it.</summary>
    private static ProcessResult ReadsTheMarker(ProcessRequest request)
    {
        var text = File.ReadAllText(Path.Combine(After(request.Arguments, "--cwd"), "CHECK.md"));

        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20_000, 30,
            $"marker: {text.Trim().Split(' ')[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT");
    }

    private PlayedKimi Played() => new(new ProcessLauncher()) { Turn = (_, request) => ReadsTheMarker(request) };

    private string OneShotFolder => Path.Combine(_homes, VibeHomes.OneShotFolder);

    /// <summary>What every ending owes: the deleter never met the junction, and the person's folder is whole.</summary>
    private void TheHomeWasDisposedJunctionFirst(IReadOnlyList<string> before)
    {
        _unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder is whole: the same names, bytes and read-only bits");
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = StandIn(_person);
        var played = Played();

        var (code, said) = await CheckAsync(KimiSettings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0, said.ToString());
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var request = played.Shims.Should().ContainSingle().Subject.Request;
        After(request.Arguments, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = After(request.Arguments, AcpWords.HomeFlag);
        home.Should().StartWith(_homes.Path);
        Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(home))).Should().Be(VibeHomes.OneShotFolder, "J3: under the short homes root");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().HaveLength(8);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        using var stood = StandIn(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinked.Add;
        var played = theTurnFails
            ? new PlayedKimi(new ProcessLauncher()) { Turn = (_, _) => new ProcessResult(70, string.Empty, "the agent failed", TimedOut: false) }
            : Played();

        await CheckAsync(KimiSettings(_homes), played, TestContext.Current.CancellationToken);

        played.Shims.Should().ContainSingle("the check launched its one turn");
        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");
        TheHomeWasDisposedJunctionFirst(before);
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = StandIn(_person);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinked.Add;
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        try
        {
            await CheckAsync(KimiSettings(_homes), played, caller.Token);
        }
        catch (OperationCanceledException)
        {
            // The check may end by raising its cancellation or by recording it; either way its home is owed back.
        }

        played.Shims.Should().ContainSingle("the turn was started, then cancelled");
        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("the home's folder goes when the check is cancelled");
        TheHomeWasDisposedJunctionFirst(before);
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = StandIn(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinked.Add;
        var played = Played();

        await CheckAsync(KimiSettings(string.Empty), played, TestContext.Current.CancellationToken);

        var home = After(played.Shims.Should().ContainSingle().Subject.Request.Arguments, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be("own", "no homes root: the home stays in the check's own scratch folder");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch, and the home in it, are gone");
        TheHomeWasDisposedJunctionFirst(before);
    }
}
