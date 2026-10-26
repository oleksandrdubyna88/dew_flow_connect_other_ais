using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs one turn as a
/// consultation makes it — in a COPY of its scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c> (or under its own
/// scratch when no root is set) — and the home is disposed junction-first whatever ended the check: an answer, a failed turn, a cancellation.
/// </summary>
/// <remarks>
/// <para>The whole mode (<c>ConsultantCheckMode.AnswerAsync</c>), git for real, the shim played in process (<see cref="PlayedKimi"/>) over a fake
/// person's sign-in (<see cref="KimiFixture"/>; the person's folder lives outside the homes root the check deletes). No test starts a real
/// <c>kimi</c>. Every folder a test makes is named <c>testsToDelete-…</c> and removed in <see cref="Dispose"/>.</para>
/// <para>"The deleter's unlink recorder" is <c>GitScratch.AfterUnlink</c>: a Kimi home disposed junction-first never lets the generic deleter
/// meet its junction, so the recorder must stay empty.</para>
/// </remarks>
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

    /// <summary>The panel's settings for a Kimi row <c>my-kimi</c> answering the <c>claude</c> caller; <paramref name="homesRoot"/> empty is a root-less check.</summary>
    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") },
    };

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    /// <summary>The person's folder, whole: the same names, bytes and read-only bits as <paramref name="before"/>.</summary>
    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    /// <summary>The value after <c>--home</c> on a shim request.</summary>
    private static string HomeOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--home") + 1];

    /// <summary>The check, in process: <c>--check-consultant --caller claude</c> over the given launcher, with the fake person's sign-in and the measured host standing in.</summary>
    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s55-check-helper test
        // Run the check as the Vibe check test does (`ConsultantCheckTests.Check`): `ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"],
        //   played, _temp, _warned.Add, Noticing.None, ct)` returns `(int Code, string Out, string Err)`; parse `Out` with `JsonDocument.Parse(Out).RootElement.Clone()`
        //   (empty Out → `default`) and return `(Code, said)`.
        // It must run INSIDE the stand-ins (the same four as `AKimiConsultationScenarioTests.StandIn`, minus `CallingAs`):
        //   `using var _ = WindowsStandIn.Begin();`
        //   `using var _lock = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));`
        //   `using var _seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));`
        // (the seams are async-local, so they reach the awaited call). Give each `using` its own distinct variable name.
        throw new NotImplementedException("s55-check-helper");
        // QWEN-FILL-END s55-check-helper
    }

    /// <summary>What the played agent answers: the marker read from the copy's CHECK.md, and the canary it was told not to read.</summary>
    private static ProcessResult AnswersFromTheCopy(ProcessRequest request)
    {
        // QWEN-FILL-BEGIN s55-answer test
        // The check's prompt wants two lines: `marker: <the marker>` and `canary: CANNOT`. The marker is the last word of the copy's `CHECK.md` with its
        // trailing '.' trimmed: `File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')`
        // (the same read `ConsultantCheckTests` does). Return `PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT")`.
        throw new NotImplementedException("s55-answer");
        // QWEN-FILL-END s55-answer
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-copy-oneshot test
        // Arrange: `var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswersFromTheCopy(request) };`, `RecordingTheDeleter()`,
        //   `var before = _person.Hash();`.
        // Act: `var (code, said) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);` (`_homes` converts to its path string).
        // Assert: `code.Should().Be(0)`; `said.GetProperty("state").GetString().Should().Be("answered", said.ToString())`; `said.GetProperty("markerRead").GetBoolean()` true
        //   (the copy holds the scratch's CHECK.md); `played.Shims` holds exactly one launch (`.Should().ContainSingle().Subject.Request`): its `WorkingDirectory`
        //   `.Should().Contain(Path.Combine("own", "tree"))` (a copy in the check's own scratch, never the repository); `HomeOf(request)` `.Should().StartWith(
        //   Path.Combine(_homes, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar)` and `Path.GetFileName(...)` `.Should().StartWith("kh-")` with 13 characters (`kh-` + 10 hex);
        //   the arguments hold no "--session"; `ThePersonsFolderIsWhole(before)`.
        //   `Directory.EnumerateDirectories(_temp, "coai-check-*")` `.Should().BeEmpty("the copy goes with the check's scratch")`.
        // Needs `using CoaiMcp.Runners.Vibe;` for `VibeHomes.OneShotFolder`. Before E5.4 the check refused the row ("cannot hold a consultation in this build"); after it only a wrong cwd or home breaks it.
        throw new NotImplementedException("s551-copy-oneshot");
        // QWEN-FILL-END s551-copy-oneshot
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        _ = theTurnFails; // the parameter is read by the body below; this line only keeps the skeleton building
        // QWEN-FILL-BEGIN s552-home-gone test
        // Arrange: `RecordingTheDeleter()`, `var before = _person.Hash();`; `var played = new PlayedKimi(new ProcessLauncher())` whose `Turn` is `AnswersFromTheCopy(request)`,
        //   or — when `theTurnFails` — `PlayedKimi.Lost(PlayedKimi.SessionOf(request))` (exit 70, a played failure).
        // Act: `await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken)`.
        // Assert: the shim was launched (`played.Shims.Should().ContainSingle()`); `Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder))`
        //   `.Should().BeEmpty("the home's folder goes when the check ends, whatever ended it")` — `coai-vh\<8>` gone; `TheDeleterNeverMetTheJunction()`; `ThePersonsFolderIsWhole(before)`.
        // Needs `using CoaiMcp.Runners.Vibe;`. Fails before E5.3 g: "Expected the deleter's unlink recorder not to be invoked …".
        throw new NotImplementedException("s552-home-gone");
        // QWEN-FILL-END s552-home-gone
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancelled test
        // Arrange: `RecordingTheDeleter()`, `var before = _person.Hash();`, `using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);`,
        //   `var started = new TaskCompletionSource();`; `var played = new PlayedKimi(new ProcessLauncher())` whose `During` is
        //   `async (_, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }` (the played shim blocks until cancelled; `During`'s token is the launch's).
        // Act: `var check = CheckAsync(Settings(_homes), played, cts.Token);` then `await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);`
        //   then `await cts.CancelAsync();` then `var (_, said) = await check;`.
        // Assert: the record says cancelled — `said.GetProperty("state").GetString()` is not "answered" and `said.ToString()` contains "cancel" (case-insensitive; read the real word off the first run
        //   and write it down in the notes); `Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder))` is empty; `TheDeleterNeverMetTheJunction()`; `ThePersonsFolderIsWhole(before)`.
        // If `CheckAsync` throws OperationCanceledException instead of returning a record, assert that with `await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check)` and still check the three folder facts.
        throw new NotImplementedException("s553-cancelled");
        // QWEN-FILL-END s553-cancelled
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        // Arrange: `RecordingTheDeleter()`, `var before = _person.Hash();`, `var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswersFromTheCopy(request) };`.
        // Act: `await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken)` — settings built by hand, no homes root.
        // Assert: one shim launch; `HomeOf(request)` contains `Path.Combine("own", "kh-")` (a Kimi home under `<scratch>\own\kh-…`, E5.5 decision 2); after the check `Directory.EnumerateDirectories(_temp, "coai-check-*")`
        //   is empty (the scratch is gone); `TheDeleterNeverMetTheJunction()`; `ThePersonsFolderIsWhole(before)`.
        // Needs the `home` path read from the request BEFORE the check ends (it is in `played.Shims`' request, which stays after the check).
        // Fails before E5.3 g: "Expected the deleter's unlink recorder not to be invoked …".
        throw new NotImplementedException("s554-rootless");
        // QWEN-FILL-END s554-rootless
    }
}
