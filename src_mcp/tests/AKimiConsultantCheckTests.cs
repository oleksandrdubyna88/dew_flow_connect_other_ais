using System.Text.Json;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation makes it, in a COPY of
/// the check's scratch repository, on a ONE-SHOT owned home (junction and all), disposed junction-first whatever ended the check. The shim is played
/// in process (<see cref="PlayedKimi"/>) over a fake person's sign-in (<see cref="KimiFixture"/>), so no turn is paid for.
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private const string OneShotFolder = "coai-vh";

    private readonly TempDir _dataDir = TempDir.For("testsToDelete-kcheck-data-");
    private readonly TempDir _tempDir = TempDir.For("testsToDelete-kcheck-temp-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinked = [];
    private readonly List<string> _linked = [];

    private string _data => _dataDir.Path;

    private string _temp => _tempDir.Path;

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _dataDir.Dispose();
        _tempDir.Dispose();
    }

    /// <summary>
    /// Set at the top of each test, not in the constructor: the hooks are per async flow, and what the check does runs in the test's own.
    /// <see cref="_linked"/> is the positive half — a home's credentials junction was unlinked — so "the deleter's recorder never fired" cannot pass
    /// because no home was made.
    /// </summary>
    private void Recording()
    {
        GitScratch.AfterUnlink = _unlinked.Add;
        KimiHome.AfterUnlink = _linked.Add;
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

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string After(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    /// <summary>The played agent reads the marker out of the scratch copy's <c>CHECK.md</c>, as a real consultant would.</summary>
    private static string Answer(ProcessRequest request)
    {
        var marker = File.ReadAllText(Path.Combine(After(request, "--cwd"), "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

        return $"marker: {marker}{Environment.NewLine}canary: CANNOT";
    }

    private static PlayedKimi Answering(Action<ProcessRequest>? seen = null) => new(new ProcessLauncher())
    {
        Turn = (_, request) =>
        {
            seen?.Invoke(request);

            return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 10, Answer(request));
        },
    };

    private static IEnumerable<string> Entries(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder) : [];

    // ---------- S5.5.1 ----------

    /// <summary>S5.5.1: the check stands in a copy of the scratch repository, on a one-shot home under the short homes root.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        Recording();
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-vh-check-");
        var played = Answering();

        var (code, said, _) = await Check(Settings(homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0, said.ToString());
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var request = played.Shims.Should().ContainSingle().Subject.Request;
        After(request, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = After(request, "--home");
        home.Should().StartWith(Path.Combine(homes, OneShotFolder) + Path.DirectorySeparatorChar, "J3: under the short homes root, never under TEMP");
        Path.GetFileName(home).Should().StartWith("kh-").And.HaveLength(13);
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
    }

    // ---------- S5.5.2 ----------

    /// <summary>S5.5.2: when the check ends, passed or failed, its home is gone junction-first and the person's folder is whole.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        Recording();
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-vh-check-");
        var played = theTurnFails
            ? new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) }
            : Answering();
        var before = person.Hash();

        var (_, said, _) = await Check(Settings(homes), played, TestContext.Current.CancellationToken);

        played.Shims.Should().ContainSingle("the check ran its one turn");
        Entries(Path.Combine(homes, OneShotFolder)).Should().BeEmpty("the home's folder goes when the check ends");
        _linked.Should().ContainSingle("a home was made, and KimiHome unlinked its credentials junction once");
        _unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        if (theTurnFails)
        {
            Text(said, "state").Should().NotBe("answered", said.ToString());
        }
    }

    // ---------- S5.5.3 ----------

    /// <summary>S5.5.3: a check cancelled mid-turn still disposes its home — the <c>finally</c> is what is pinned.</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        Recording();
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var homes = TempDir.For("testsToDelete-vh-check-");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };
        var before = person.Hash();
        using var stop = new CancellationTokenSource();

        var checking = Check(Settings(homes), played, stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        var (code, said, _) = await checking;

        code.Should().Be(0, said.ToString());
        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled", "a cancelled check says so, as the one cancelled while its scratch is made does");
        Entries(Path.Combine(homes, OneShotFolder)).Should().BeEmpty("a cancelled check gives its home back");
        _linked.Should().ContainSingle("a home was made, and KimiHome unlinked its credentials junction once");
        _unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        person.Hash().Should().Equal(before);
    }

    // ---------- S5.5.4 ----------

    /// <summary>S5.5.4: a check built without a homes root keeps its home in its own scratch, which goes junction-first with it.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        Recording();
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var home = string.Empty;
        var played = Answering(request => home = After(request, "--home"));

        var (code, said, _) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        code.Should().Be(0, said.ToString());
        home.Should().Contain(Path.Combine("own", "kh-"), "rootless, the home lives in the check's own scratch");
        Directory.Exists(Path.GetDirectoryName(home)).Should().BeFalse("the scratch is gone");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty();
        _linked.Should().ContainSingle("a home was made, and KimiHome unlinked its credentials junction once");
        _unlinked.Should().BeEmpty("the scratch's deleter never met the junction");
    }
}
