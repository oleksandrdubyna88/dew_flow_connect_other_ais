using System.Text.Json;
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
/// <c>coai-mcp --check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation
/// makes it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>, disposed junction-first
/// whatever ended the check. The check's own invariant (the scratch repository unchanged, the canary untouched) holds.
/// </summary>
/// <remarks>
/// <para>No new branch in the check: it already gives a copy to a runtime that asks (<c>NeedsASanitisedSnapshot</c>) and a one-shot home to a runtime that
/// declares a <c>HomesFolder</c>, which <see cref="KimiConsultant"/> does (E5.4 decision 6). What makes it safe for Kimi is E5.3 decision g, so these tests
/// prove the PATH. The shim is played (<see cref="PlayedKimi"/>) over a fake person (<see cref="KimiFixture"/>) — no turn is paid for and the real
/// <c>kimi</c> is never started. Every folder is a <c>testsToDelete-</c> folder of the test's own; the fake person lives OUTSIDE the homes root.</para>
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _data.Dispose();
        _temp.Dispose();
        _homes.Dispose();
        _person.Dispose();
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

    private string OneShotRoot => Path.Combine(_homes, VibeHomes.OneShotFolder);

    /// <summary>The one-shot folder of the check (<c>coai-vh\&lt;8&gt;</c>) is gone; the root it was made under may stay.</summary>
    private void TheOneShotFolderIsGone() =>
        (Directory.Exists(OneShotRoot) ? Directory.EnumerateFileSystemEntries(OneShotRoot) : []).Should().BeEmpty("coai-vh\\<8> is gone");

    /// <summary>The settings of a check whose caller is a Kimi row <c>my-kimi</c>, with the homes root given (empty: a check built by hand, rootless).</summary>
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

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, as the service scenarios do.</summary>
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

    /// <summary>The check, run through <c>ConsultantCheckMode.AnswerAsync</c> with args <c>--check-consultant --caller claude</c>; the exit code and the JSON it printed.</summary>
    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    /// <summary>The played agent reads <c>CHECK.md</c> in the folder the shim was started in and answers with the word it holds, as the check asks.</summary>
    private static ProcessResult ReadsTheMarker(int _, ProcessRequest request)
    {
        var words = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);

        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20000, 30, $"marker: {words[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT");
    }

    private static string ArgumentAfter(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = StandIn();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = ReadsTheMarker };
        var before = _person.Hash();

        var (code, said, _) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the played agent read CHECK.md in the folder it stood in");

        var request = played.Shims.Should().ContainSingle().Subject.Request;
        var cwd = ArgumentAfter(request, "--cwd");
        cwd.Should().Contain(Path.Combine("own", "tree"));
        request.WorkingDirectory.Should().Be(cwd);

        var home = ArgumentAfter(request, "--home");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(OneShotRoot);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");

        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch repository is deleted at the end");
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        using var stood = StandIn();
        RecordingTheDeleter();
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = theTurnFails ? (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) : ReadsTheMarker,
        };

        var (_, said, _) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);

        said.GetProperty("state").GetString().Should().Be(theTurnFails ? "failed" : "answered", said.ToString());
        TheOneShotFolderIsGone();
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = StandIn();
        RecordingTheDeleter();
        var before = _person.Hash();
        using var cancelling = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await cancelling.CancelAsync();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };

        var (code, said, _) = await Check(Settings(_homes), played, cancelling.Token);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("failed", said.ToString());
        said.GetProperty("failureKind").GetString().Should().Be("cancelled");
        played.Shims.Should().ContainSingle("the turn had started when the check was cancelled");
        TheOneShotFolderIsGone();
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = StandIn();
        RecordingTheDeleter();
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = ReadsTheMarker };

        await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        var home = ArgumentAfter(played.Shims.Should().ContainSingle().Subject.Request, "--home");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be("own", "no root: the home stays in the check's scratch");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch is gone with its home");
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }
}
