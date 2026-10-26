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
/// <c>coai-mcp --check-consultant</c> for a Kimi row, proved on its own path (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>,
/// S5.5.1–S5.5.4): the check runs in a COPY of the scratch repository when the runtime asks for a sanitised snapshot, its home is a ONE-SHOT
/// home under the homes root (or under the scratch when the root is empty), and that home is disposed junction-first — also when the turn
/// fails and when the check is cancelled mid-turn. The person's sign-in is whole after every case.
/// </summary>
/// <remarks>
/// Only the Kimi shim is played (<see cref="PlayedKimi"/>); git runs for real; the person is a fake (<see cref="KimiFixture"/>) reached through
/// the Kimi seams and the sign-in lock root, stood in per async flow (<see cref="WindowsStandIn"/>, <see cref="KimiSeams.Using"/>).
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _data = TempDir.For("testsToDelete-kcheck-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kcheck-temp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kcheck-homes-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinked = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _person.Dispose();
        _data.Dispose();
        _temp.Dispose();
        _homes.Dispose();
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

    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);

        return (code, JsonDocument.Parse(stdout).RootElement.Clone());
    }

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    /// <summary>The marker the played agent reads from the copy's <c>CHECK.md</c>, answered as an answered turn.</summary>
    private static ProcessResult Answers(ProcessRequest request)
    {
        var cwd = After(request.Arguments, "--cwd");
        var word = File.ReadAllText(Path.Combine(cwd, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 10, $"marker: {word}{Environment.NewLine}canary: CANNOT");
    }

    /// <summary>A turn that loses its session: exit 70, the one-shot home's turn fails.</summary>
    private static ProcessResult LoseSession(ProcessRequest request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request));

    private static readonly Func<ProcessRequest, CancellationToken, Task> NoWait = static (_, _) => Task.CompletedTask;

    private static PlayedKimi Played(Func<ProcessRequest, ProcessResult>? turn = null, Func<ProcessRequest, CancellationToken, Task>? during = null) =>
        new(new ProcessLauncher())
        {
            Turn = (_, request) => (turn ?? Answers)(request),
            During = during ?? NoWait,
        };

    /// <summary>The homes root's one-shot folder, <c>coai-vh</c> — it may not exist at all once every home is gone.</summary>
    private string OneShotFolder => Path.Combine(_homes, VibeHomes.OneShotFolder);

    private IReadOnlyList<string> EntriesOfOneShotFolder() =>
        Directory.Exists(OneShotFolder) ? Directory.EnumerateFileSystemEntries(OneShotFolder).ToList() : [];

    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinked.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var windows = WindowsStandIn.Begin();
        using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));
        var before = _person.Hash();
        var played = Played();

        var (code, said) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var launch = played.Shims.Should().ContainSingle().Subject;
        var cwd = After(launch.Request.Arguments, "--cwd");
        cwd.Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the scratch repository");
        var home = After(launch.Request.Arguments, "--home");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$", "a one-shot Kimi home named by the runtime");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(OneShotFolder, "coai-vh\\<8>\\kh-…: under the homes root, never under TEMP");
        said.GetProperty("failureKind").GetString().Should().BeNullOrEmpty("the check's invariant held: never failed with tree-changed");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch repository and its copy are gone");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool lost)
    {
        using var windows = WindowsStandIn.Begin();
        using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));
        RecordingTheDeleter();
        var before = _person.Hash();
        var played = Played(turn: lost ? LoseSession : null);

        var (_, said) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        if (lost)
        {
            said.GetProperty("state").GetString().Should().NotBe("answered", said.ToString());
        }
        else
        {
            said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        }

        EntriesOfOneShotFolder().Should().BeEmpty("coai-vh\\<8> is gone once the check ends");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var windows = WindowsStandIn.Begin();
        using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));
        RecordingTheDeleter();
        var before = _person.Hash();
        var started = new TaskCompletionSource();
        var played = Played(during: async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = CheckAsync(Settings(_homes), played, cts.Token);
        // Bounded: a check that never reaches its shim must fail here, not hang the run.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        var (_, said) = await running;

        said.GetProperty("failureKind").GetString().Should().Be("cancelled", said.ToString());
        EntriesOfOneShotFolder().Should().BeEmpty("the home is disposed although the turn was cancelled");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var windows = WindowsStandIn.Begin();
        using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));
        RecordingTheDeleter();
        var before = _person.Hash();
        var played = Played();

        var (code, said) = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        var home = After(played.Shims.Should().ContainSingle().Subject.Request.Arguments, "--home");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "<scratch>\\own\\kh-…: the home sits in the check's scratch");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch is gone");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }
}
