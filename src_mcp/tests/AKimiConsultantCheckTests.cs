using System.Text.Json;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation makes
/// it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home (junction and all) under <c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>,
/// disposed junction-first whatever ended the check; and the check's own invariant (the scratch repository unchanged, the canary untouched) holds.
/// </summary>
/// <remarks>
/// The check mode runs in process (<see cref="ConsultantCheckMode.AnswerAsync"/>), git for real, the Kimi shim played (<see cref="PlayedKimi"/>) over a
/// fake person's sign-in (<see cref="KimiFixture"/>); no turn is paid for and the real <c>kimi</c> never starts. Shaped like the Vibe checks of
/// <see cref="ConsultantCheckTests"/>. The person's folder and the homes root are test folders named <c>testsToDelete-…</c>.
/// </remarks>
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
        _homes.Dispose();
        _person.Dispose();
        foreach (var dir in (string[])[_data, _temp])
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    /// <summary>The settings of a check of one Kimi row; <paramref name="homesRoot"/> empty is a hand-built, root-less one (S5.5.4).</summary>
    private PanelSettings Settings(string homesRoot, TimeSpan? timeout = null) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = timeout ?? TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") },
    };

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>The value after <paramref name="flag"/> in a shim request's arguments.</summary>
    private static string After(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    /// <summary>A played Kimi whose answer reads the check's marker in the folder the shim stood in — what a consultation's turn does (see <c>AVibeCheck_…</c>).</summary>
    private PlayedKimi Played(Func<ProcessRequest, ProcessResult>? turn = null)
    {
        // QWEN-FILL-BEGIN s55-played test
        // Return `new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => ... }` where, when `turn` is null, the turn reads the marker:
        // `File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')` and answers
        // `PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT")`; when `turn` is not null the
        // Turn is `(_, request) => turn(request)`.
        throw new NotImplementedException("s55-played");
        // QWEN-FILL-END s55-played
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-check test
        // Arrange: `using var stood = AKimiConsultationScenarioTests.StandIn(_person);` (the fake person, the host and the lock root the adapter sees);
        // `var played = Played();`. Windows only: `Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");` first.
        // Act: `var (code, said, _) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);` (TempDir converts to string implicitly, as
        // the Vibe check test passes `homes`).
        // Assert: code Should().Be(0); Text(said, "state") Should().Be("answered", said.ToString()); said.GetProperty("markerRead").GetBoolean() Should().BeTrue();
        // `var turn = played.Shims.Should().ContainSingle().Subject.Request;` turn.Arguments: `After(turn, "--cwd")` Should().Contain(Path.Combine("own", "tree"));
        // `var home = After(turn, AcpWords.HomeFlag);` home Should().StartWith(Path.Combine(_homes, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar);
        // Path.GetFileName(home) Should().MatchRegex("^kh-[0-9a-f]{10}$"); the home's grandparent folder is `coai-vh` (Path.GetDirectoryName twice) — the `<8>` folder is its child.
        // The scratch repository and its canary unchanged: the mode's own invariant — `said` carries `canaryTouched`/`treeChanged` style fields; READ the Vibe check
        // test and ConsultantCheckTests.ACanaryTheConsultantChanged_IsATreeChange_NotAnAnswer in the pack and assert the "answered" state is enough if no field exists.
        // Fails before E5.4 with: Expected the check of my-kimi to pass, but it failed: the vendor 'my-kimi' runs on 'kimi', which cannot hold a consultation in this build.
        throw new NotImplementedException("s551-check");
        // QWEN-FILL-END s551-check
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        // QWEN-FILL-BEGIN s552-gone test
        // Arrange: as S5.5.1 (stand-in, `Played(...)`); `var before = _person.Hash();` `RecordingTheDeleter();`. When `theTurnFails`, the played turn is
        // `request => new ProcessResult(70, string.Empty, "the played shim failed" + Environment.NewLine, TimedOut: false)` (exit 70, as the shim's
        // `agent_failed`); else the default answering turn. Windows only: `Assert.SkipUnless(OperatingSystem.IsWindows(), ...)` first.
        // Act: `await Check(Settings(_homes), played, TestContext.Current.CancellationToken)`. In the failing arm do NOT assert the state is answered.
        // Assert: `Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder))` Should().BeEmpty("the home's folder goes when the check
        // ends, whatever ended it"); `_unlinkedByTheDeleter` Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the
        // junction first"); `_person.Hash()` Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before").
        // Fails before E5.3 g with: Expected the deleter's unlink recorder not to be invoked …
        throw new NotImplementedException("s552-gone " + theTurnFails);
        // QWEN-FILL-END s552-gone
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancel test
        // Arrange: stand-in as S5.5.1; `RecordingTheDeleter();` `var before = _person.Hash();` `using var caller = CancellationTokenSource.CreateLinkedTokenSource(
        // TestContext.Current.CancellationToken);` a PlayedKimi built with `new PlayedKimi(new ProcessLauncher()) { During = async (_, ct) => { await caller.CancelAsync();
        // ct.ThrowIfCancellationRequested(); } }` (so the shim "blocks" until cancelled). Windows only: SkipUnless first.
        // Act: `var (_, said, _) = await Check(Settings(_homes), played, caller.Token);` — the check answers a cancelled RECORD rather than throwing (see
        // ConsultantCheckTests.ACheckCancelledWhileItsScratchIsMade_IsCancelled_NotUnavailable in the pack for the exact state word and the call shape).
        // Assert: Text(said, "state") Should().Be("failed", said.ToString()) and Text(said, "failureKind") Should().Be("cancelled") (the words the existing cancelled
        // check test uses); `Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder))`
        // Should().BeEmpty(); `_unlinkedByTheDeleter` Should().BeEmpty(); `_person.Hash()` Should().Equal(before).
        // Green after E5.3 g (it pins the `finally`); not red against the present tree.
        throw new NotImplementedException("s553-cancel");
        // QWEN-FILL-END s553-cancel
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        // Arrange: stand-in as S5.5.1; `RecordingTheDeleter();` `var before = _person.Hash();` `var played = Played();` settings built by hand with NO root:
        // `Settings(string.Empty)`. Windows only: SkipUnless first.
        // Act: `var (code, said, _) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);`
        // Assert: code Should().Be(0); `var turn = played.Shims.Should().ContainSingle().Subject.Request;` `After(turn, AcpWords.HomeFlag)` Should().Contain(Path.Combine("own", "kh-"))
        // (the home is `<scratch>\own\kh-<10 hex>`) — and Path.GetFileName(home) MatchRegex("^kh-[0-9a-f]{10}$"); after the check
        // `Directory.EnumerateDirectories(_temp, "coai-check-*")` Should().BeEmpty("the scratch is gone"); `_unlinkedByTheDeleter` Should().BeEmpty();
        // `_person.Hash()` Should().Equal(before).
        // Fails before E5.3 g with: Expected the deleter's unlink recorder not to be invoked …
        throw new NotImplementedException("s554-rootless");
        // QWEN-FILL-END s554-rootless
    }
}
