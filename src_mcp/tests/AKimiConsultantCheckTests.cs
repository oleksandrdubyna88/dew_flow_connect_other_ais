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
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn in a COPY of the check's
/// scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c> (or, with no homes root, under the check's own scratch), disposed
/// junction-first whatever ended the check. No new branch of the check serves it (decision 1): the check already gives a copy to a runtime that asks
/// and a one-shot home to a runtime that declares a <c>HomesFolder</c>, and E5.3's decision g is what makes the disposal safe.
/// </summary>
/// <remarks>
/// The real check, the real git and the real launch control over a fake person's sign-in (<see cref="KimiFixture"/>) — only the shim is played
/// (<see cref="PlayedKimi"/>), so no turn is paid for and no <c>kimi</c> is started. The person's folder, the homes root, the check's temp root and its
/// data directory are each a <c>testsToDelete-</c> folder of this test, and the person's lives outside the homes root the check deletes.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _data.Dispose();
        _temp.Dispose();
        _homes.Dispose();
        _person.Dispose();
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

    /// <summary>
    /// The person, the host and the lock root this flow's Kimi adapter sees — set by SYNCHRONOUS calls, so the values reach the test's own flow and the
    /// check it awaits (<see cref="KimiSeams.Using"/>, the <c>VibePathBudget.Using</c> precedent).
    /// </summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    /// <summary>The played agent answers as a model that read <c>CHECK.md</c> in ITS working folder and could not read the canary.</summary>
    private static ProcessResult ReadsTheMarker(ProcessRequest request) =>
        PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31,
            $"marker: {File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT");

    private async Task<JsonElement> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (_, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _ => { }, Noticing.None, ct);

        return JsonDocument.Parse(stdout).RootElement.Clone();
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    private string OneShotFolder => Path.Combine(_homes, VibeHomes.OneShotFolder);

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var standIn = StandIn();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => ReadsTheMarker(request) };

        var said = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md, and the check's own invariant (the scratch repository and its canary unchanged) held");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        var cwd = After(shim.Arguments, "--cwd");
        cwd.Should().StartWith(_temp.Path).And.Contain(Path.Combine(ConsultantCheck.OwnName, "tree") + Path.DirectorySeparatorChar,
            "a copy under the check's own scratch, never the scratch repository");
        shim.WorkingDirectory.Should().Be(cwd);
        var home = After(shim.Arguments, "--home");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(OneShotFolder, "J3: the check's home is a one-shot folder under the homes root, never under TEMP");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().MatchRegex("^[0-9a-f]{8}$");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData("passed")]
    [InlineData("exit 70")]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(string ending)
    {
        using var standIn = StandIn();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => ending == "passed" ? ReadsTheMarker(request) : PlayedKimi.Lost(PlayedKimi.SessionOf(request)),
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        var said = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        (Text(said, "state") == "answered").Should().Be(ending == "passed", said.ToString());
        played.Shims.Should().ContainSingle("the turn really ran, so a home was made and a junction to the sign-in with it");
        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("coai-vh\\<8> goes when the check ends, whatever ended it");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var standIn = StandIn();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = new TaskCompletionSource();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                running.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        var checking = CheckAsync(Settings(_homes), played, cancel.Token);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await cancel.CancelAsync();
        var said = await checking;

        Text(said, "failureKind").Should().Be("cancelled", said.ToString());
        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("the finally gives the one-shot folder back on a cancellation too");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty();
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before);
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var standIn = StandIn();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => ReadsTheMarker(request) };
        var before = _person.Hash();
        RecordingTheDeleter();

        var said = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        var home = After(shim.Arguments, "--home");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        home.Should().StartWith(_temp.Path, "no root: the home stays in the check's own scratch");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "<scratch>\\own\\kh-…");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch goes whole");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before);
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
}
