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
/// <c>--check-consultant</c> of a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation
/// makes it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c> — and the
/// home gone junction-first whatever ended the check. The check itself gained no Kimi branch: it already gives a copy to a runtime that asks and a
/// one-shot home to a runtime that declares a <c>HomesFolder</c> (E5.4 decision 6), and E5.3 decision g made the disposal safe.
/// </summary>
/// <remarks>
/// The shim is played (<see cref="PlayedKimi"/>) over a fake person's sign-in (<see cref="KimiFixture"/>); git runs for real, in this test's own
/// folders. No test starts a real <c>kimi</c>, and every junction points inside the fixture's own <c>testsToDelete-</c> folder.
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-temp-").FullName;
    private readonly List<string> _unlinkedByTheDeleter = [];
    private readonly List<string> _warned = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        foreach (var folder in (string[])[_data, _temp])
        {
            TryDelete(folder);
        }

        _person.Dispose();
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover testsToDelete- folder is the sweep's, not a failing test.
        }
    }

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

    /// <summary>The marker the played agent "reads" from the copy the check made: the word <c>CHECK.md</c> ends with.</summary>
    private static string MarkerIn(ProcessRequest request) =>
        File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

    private static ProcessResult Reads(ProcessRequest request) =>
        PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 5, $"marker: {MarkerIn(request)}{Environment.NewLine}canary: CANNOT");

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    private async Task<JsonElement> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        using var standIn = WindowsStandIn.Begin();
        using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        var (_, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);

        return stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default;
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string After(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var homes = TempDir.For("testsToDelete-kimi-check-homes-");
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => Reads(request) };

        var said = await CheckAsync(Settings(homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        Text(said, "canary").Should().Be("not-attempted", "the scratch repository and its canary were left as they were");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        After(shim, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository itself");
        var home = After(shim, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(Path.Combine(homes, VibeHomes.OneShotFolder), "J3: <root>\\coai-vh\\<8>\\kh-<10 hex>");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().MatchRegex("^[0-9a-f]{8}$");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        using var homes = TempDir.For("testsToDelete-kimi-check-homes-");
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => theTurnFails ? PlayedKimi.Lost(PlayedKimi.SessionOf(request)) : Reads(request),
        };

        var said = await CheckAsync(Settings(homes), played, TestContext.Current.CancellationToken);

        (Text(said, "state") == "answered").Should().Be(!theTurnFails, said.ToString());
        played.Shims.Should().ContainSingle("one turn was launched, whatever it ended in");
        Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var homes = TempDir.For("testsToDelete-kimi-check-homes-");
        var before = _person.Hash();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var blocked = new TaskCompletionSource();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                blocked.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };

        var checking = CheckAsync(Settings(homes), played, cancel.Token);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await cancel.CancelAsync();
        var said = await checking.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled");
        Directory.EnumerateFileSystemEntries(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("a cancelled check still gives its home back");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before);
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => Reads(request) };

        var said = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        var home = After(played.Shims.Should().ContainSingle().Subject.Request, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "with no homes root the home stays in the check's own folder");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch goes whole, its home first");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before);
    }
}
