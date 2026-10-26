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
/// <c>coai-mcp --check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs
/// one turn as a consultation makes it — in a COPY of the check's scratch repository, on a ONE-SHOT home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>
/// (or <c>&lt;scratch&gt;\own</c> when the settings carry no root) — and disposes the home junction-first whatever ended the check.
/// </summary>
/// <remarks>
/// The whole mode in process (<see cref="ConsultantCheckMode.AnswerAsync"/>), git for real, the Kimi shim played (<see cref="PlayedKimi"/>) over a
/// fake person (<see cref="KimiFixture"/>) — no turn is paid for and no real <c>kimi</c> starts. Every junction points into the fixture's own
/// <c>testsToDelete-kimi-</c> folder; every folder the check makes lives under this test's own <c>testsToDelete-</c> folders.
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _person.Dispose();
        _homes.Dispose();
        _temp.Dispose();
        _data.Dispose();
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    private static IDisposable StandIn(KimiFixture person)
    {
        // QWEN-FILL-BEGIN s55-standin test
        // The person, the host and the lock root this flow's Kimi adapters see — a SYNCHRONOUS call whose result the test holds in a `using`.
        // Copy AKimiConsultationScenarioTests.StandIn (see the context pack): a Scopes object (nested private sealed class, copy it too) holding
        // WindowsStandIn.Begin(), KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        // KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))). No CallingAs here: the check names its caller on the argv.
        // The method is static, so it takes the person as a parameter; the tests call `StandIn(_person)`.
        throw new NotImplementedException("s55-standin");
        // QWEN-FILL-END s55-standin
    }

    private PanelSettings Settings(string homesRoot)
    {
        // QWEN-FILL-BEGIN s55-settings test
        // PanelSettings for a Kimi consultant row: Providers = [], DataDir = _data, ReviewerTimeout = 60 s, ConsultEnabled = true,
        // ConsultantHomesRoot = homesRoot (empty string = the root-less check), Consultants = { [CallerIdentity.Claude] = new ConsultantChoice("my-kimi", Runtime: "kimi") }
        // (no ExecutablePath: the KimiSeams shim is the executable). Return it with the object initialiser, as ConsultantCheckTests.Settings does.
        throw new NotImplementedException("s55-settings");
        // QWEN-FILL-END s55-settings
    }

    private Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s55-check test
        // Run the whole mode and parse its stdout: `var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"],
        // played, _temp, _warned.Add, Noticing.None, ct);` then return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default).
        // `played` is the PlayedKimi (an IProcessLauncher) — it is the launcher; git runs for real through its inner launcher.
        throw new NotImplementedException("s55-check");
        // QWEN-FILL-END s55-check
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551 test
        // (The method is `Task`, not `async Task`, only so the skeleton builds: make it `async Task` and use `await`.)
        // Arrange: `using var stood = StandIn(_person);` `var played = new PlayedKimi(new ProcessLauncher());` whose `Turn` answers with the marker the check
        // planted: PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, answer) where the answer is built like ConsultantCheckTests'
        // AVibeCheck test does ("marker: <last word of CHECK.md>\ncanary: CANNOT") — read CHECK.md from the shim request's WorkingDirectory
        // (File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")); `Turn` is a Func<int, ProcessRequest, ProcessResult>, so it can read the
        // copy while the check is still running). RecordingTheDeleter(). Settings(_homes).
        // Act: `var (code, said) = await CheckAsync(settings, played, TestContext.Current.CancellationToken);`
        // Assert: code 0; said.GetProperty("state").GetString() is ConsultCheckStates.Answered; markerRead true; played.Shims holds ONE launch; its Request.Arguments
        // hold "--cwd" followed by a path containing Path.Combine("own", "tree"); "--home" followed by a path that starts with
        // Path.Combine(_homes, VibeHomes.OneShotFolder) + separator, whose last segment starts with "kh-" and has 3 chars + 10 hex; the scratch repository
        // (Directory.EnumerateDirectories(_temp, "coai-check-*")) is gone, so "unchanged" is asserted through the copy: the shim's cwd is NOT the scratch repository's own folder.
        // Why it fails before E5.4: the check refuses a runtime that cannot hold a consultation; now it fails only if the check puts the home or cwd elsewhere.
        throw new NotImplementedException("s551");
        // QWEN-FILL-END s551
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        // QWEN-FILL-BEGIN s552 test
        // (`theTurnFails` is read by the line below only so the xUnit1026 analyzer lets the skeleton build; the real body reads it.)
        _ = theTurnFails;
        // (The method is `Task`, not `async Task`, only so the skeleton builds: make it `async Task` and use `await`.)
        // Arrange: `using var stood = StandIn(_person);` played = new PlayedKimi(new ProcessLauncher()) whose Turn answers as s551, or — when theTurnFails — returns
        // new ProcessResult(70, KimiShimScripts.Report(AcpEnd.SessionLost, string.Empty, "lost", tokens: false, session: PlayedKimi.Minted), string.Empty, TimedOut: false)
        // (exit 70; see PlayedKimi.Lost for the shape). `var before = _person.Hash();` RecordingTheDeleter().
        // Act: CheckAsync(Settings(_homes), played, ct).
        // Assert: Directory.Exists(Path.Combine(_homes, VibeHomes.OneShotFolder)) is false OR it holds no entries (the 8-hex folder is gone:
        // Directory.EnumerateFileSystemEntries(...).Should().BeEmpty()); TheDeleterNeverMetTheJunction(); `_person.Hash().Should().Equal(before)` (the person's folder: same
        // names, bytes and ReadOnly bits). For theTurnFails the state is "failed".
        // Why it fails before E5.3 g: the one-shot home was deleted by the bare deleter, which unlinks the junction itself (AfterUnlink fires).
        throw new NotImplementedException("s552");
        // QWEN-FILL-END s552
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553 test
        // (The method is `Task`, not `async Task`, only so the skeleton builds: make it `async Task` and use `await`.)
        // Arrange: `using var stood = StandIn(_person);` a CancellationTokenSource `cts`; a TaskCompletionSource `started`; played = new PlayedKimi(new ProcessLauncher()) { During = async (_, ct) => { started.SetResult();
        // await Task.Delay(Timeout.Infinite, ct); } } (the shim blocks until it is cancelled). RecordingTheDeleter().
        // Act: `var running = CheckAsync(Settings(_homes), played, cts.Token);` `await started.Task;` `await cts.CancelAsync();` `var (_, said) = await running;`
        // Assert: said's "state" is ConsultCheckStates.Failed and its "failureKind" is ConsultFailureKinds.Cancelled (namespace CoaiMcp.Runners.Consultation);
        // the folder Path.Combine(_homes, VibeHomes.OneShotFolder) holds no entry; TheDeleterNeverMetTheJunction().
        // Green once E5.3 g is in (pins the `finally`).
        throw new NotImplementedException("s553");
        // QWEN-FILL-END s553
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554 test
        // (The method is `Task`, not `async Task`, only so the skeleton builds: make it `async Task` and use `await`.)
        // Arrange: `using var stood = StandIn(_person);` played = new PlayedKimi(new ProcessLauncher()) answering as s551; RecordingTheDeleter(); settings = Settings(string.Empty) — no homes root.
        // Act: CheckAsync(settings, played, ct).
        // Assert: the single shim launch's "--home" argument contains Path.Combine("own", "kh-") (the home is under `<scratch>\own\kh-<10 hex>`); after the check,
        // Directory.EnumerateDirectories(_temp, "coai-check-*") is empty (the scratch is gone); TheDeleterNeverMetTheJunction(); the person's folder whole (_person.Hash() unchanged).
        // Why it fails before E5.3 g: ConsultCheckScratch.Delete used the bare deleter, which meets the junction.
        throw new NotImplementedException("s554");
        // QWEN-FILL-END s554
    }
}
