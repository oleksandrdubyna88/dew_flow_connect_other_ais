using System.Text.Json;
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
/// it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>), disposed
/// junction-first whatever ended the check. There is no new branch in the check (decision 1): these tests prove the existing path with a new adapter.
/// </summary>
/// <remarks>
/// The whole mode in process (<see cref="ConsultantCheckMode.AnswerAsync"/>), git for real, the real launch control over a fake person
/// (<see cref="KimiFixture"/>, stood in per async flow by <see cref="AKimiConsultationScenarioTests.StandIn"/>), only the shim played
/// (<see cref="PlayedKimi"/>) — no turn is paid for and the real <c>kimi</c> is never started. Every folder is a <c>testsToDelete-</c> folder of the test.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-check-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public AKimiConsultantCheckTests() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();
    }

    /// <summary>The homes root of the scenario — inside its own folder, never the machine's local data.</summary>
    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    /// <summary>The check's temp folder: <c>coai-check-*</c> scratch folders are made here.</summary>
    private string Temp => Directory.CreateDirectory(_dir.At("temp")).FullName;

    /// <summary>The panel settings of a check of a Kimi row; <paramref name="homesRoot"/> is empty for the rootless arm (S5.5.4).</summary>
    private PanelSettings Settings(string homesRoot)
    {
        // QWEN-FILL-BEGIN s55-settings test
        // Return `new PanelSettings { Providers = [], DataDir = _dir.At("data"), ReviewerTimeout = TimeSpan.FromSeconds(60), ConsultEnabled = true,
        //   ConsultantHomesRoot = homesRoot, Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") } }`.
        // (The shape is `ConsultantCheckTests.Settings` + `AKimiConsultationScenarioTests.OnKimi`; see the context pack.)
        throw new NotImplementedException("s55-settings");
        // QWEN-FILL-END s55-settings
    }

    /// <summary>The played shim: it reads the marker from the copy's CHECK.md and answers <c>marker: …</c> and <c>canary: CANNOT</c>, as the check wants.</summary>
    private static PlayedKimi Played()
    {
        // QWEN-FILL-BEGIN s55-played test
        // Return `new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, answer) }` where
        // `answer` is built from the request: the shim's working folder is `request.WorkingDirectory` (the copy's tree); read
        // `File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')` as the marker and answer
        // `$"marker: {marker}{Environment.NewLine}canary: CANNOT"` (the Vibe check test does the same from `seen.Files["CHECK.md"]`).
        // Write the lambda as a block body so the file can be read before the answer is formed.
        throw new NotImplementedException("s55-played");
        // QWEN-FILL-END s55-played
    }

    /// <summary>Runs the check as the CLI mode does, with <paramref name="launcher"/> in front of the shim: the exit code, the printed JSON, and the cancellation token it was given.</summary>
    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi launcher, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s55-check test
        // `var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], launcher, Temp, _warned.Add, Noticing.None, ct);`
        // return `(code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default)` — the shape of `ConsultantCheckTests.Check`.
        throw new NotImplementedException("s55-check");
        // QWEN-FILL-END s55-check
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-copy test
        // Arrange: `using var stood = AKimiConsultationScenarioTests.StandIn(_person); var played = Played();`
        // Act: `var (code, said) = await CheckAsync(Settings(Homes), played, TestContext.Current.CancellationToken);`
        // Assert: code == 0; said.GetProperty("state").GetString() == "answered" (put `said.ToString()` as the because-message); said "markerRead" is true;
        // `var shim = played.Shims.Should().ContainSingle().Subject.Request;`
        // the value after "--cwd" in shim.Arguments contains `Path.Combine("own", "tree")` and lies under `Temp` (the copy in the check's own scratch, never the repository);
        // the value after "--home" (VALUE of the "--home" argument): `Path.GetFileName(...)` matches "^kh-[0-9a-f]{10}$" and
        // `Path.GetDirectoryName(Path.GetDirectoryName(home))` == `Path.Combine(Homes, VibeHomes.OneShotFolder)` (the folder is `<root>\coai-vh\<8>\kh-<10 hex>`);
        // the scratch repository and its canary unchanged: the check's own invariant says so — `said.GetProperty("state")` "answered" already means it; do
        // not add more. `Directory.EnumerateDirectories(Temp, "coai-check-*")` is empty after the check (the copy goes with the scratch).
        // Fails before E5.4: "the vendor 'my-kimi' runs on 'kimi', which cannot hold a consultation in this build" (state is not "answered").
        throw new NotImplementedException("s551-copy");
        // QWEN-FILL-END s551-copy
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData("passed")]
    [InlineData("exit 70")]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(string ending)
    {
        ending.Should().NotBeNullOrEmpty("the arm's name");
        // QWEN-FILL-BEGIN s552-gone test
        // Arrange: `using var stood = AKimiConsultationScenarioTests.StandIn(_person); var before = _person.Hash();` and the played shim: for "passed" `Played()`;
        // for "exit 70" a `new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) }` (end session_lost, exit 70).
        // `_unlinkedByTheDeleter` is already recording `GitScratch.AfterUnlink` (constructor).
        // Act: `var (_, said) = await CheckAsync(Settings(Homes), played, TestContext.Current.CancellationToken);`
        // Assert: `Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder))` is empty ("coai-vh\<8> is gone"; the folder `coai-vh` itself may exist);
        // `_unlinkedByTheDeleter` is empty ("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        // `_person.Hash().Should().Equal(before)` (the person's folder: names, bytes and read-only bits). For "passed" also said "state" == "answered".
        // Fails before E5.3 g: "Expected the deleter's unlink recorder not to be invoked …".
        throw new NotImplementedException("s552-gone");
        // QWEN-FILL-END s552-gone
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancel test
        // Arrange: `using var stood = AKimiConsultationScenarioTests.StandIn(_person); var before = _person.Hash();`
        // `using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);`
        // a played shim that blocks until the token is cancelled: `new PlayedKimi(new ProcessLauncher()) { During = async (_, ct) => { await caller.CancelAsync(); ct.ThrowIfCancellationRequested(); } }`
        // (the `During` seam runs while the shim is "alive"; it is `Func<ProcessRequest, CancellationToken, Task>`).
        // Act: `var (_, said) = await CheckAsync(Settings(Homes), played, caller.Token);` — the mode answers a record, it does not throw; if it throws
        // OperationCanceledException instead, wrap the call in try/catch (OperationCanceledException) and go on to the asserts.
        // Assert: when `said` has a "state" property its value is not "answered" (the record says cancelled: `said.GetProperty("failureKind")` is "cancelled" if present);
        // `Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder))` is empty; `_unlinkedByTheDeleter` is empty; `_person.Hash()` equals `before`.
        // Passes once E5.3 g is in (it pins `finally`); red only if the disposal were skipped on cancellation.
        throw new NotImplementedException("s553-cancel");
        // QWEN-FILL-END s553-cancel
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        // Arrange: `using var stood = AKimiConsultationScenarioTests.StandIn(_person); var before = _person.Hash(); var played = Played();`
        // Settings built by hand with NO homes root: `Settings(string.Empty)`.
        // Act: `var (_, said) = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);`
        // Assert: said "state" == "answered"; the shim's "--home" VALUE, `Path.GetFileName(...)` matches "^kh-[0-9a-f]{10}$" and its parent folder name is
        // `ConsultantCheck.OwnName` ("own") — i.e. the home lives under `<scratch>\own\kh-…`; after the check `Directory.EnumerateDirectories(Temp, "coai-check-*")` is
        // empty (the scratch is gone); `_unlinkedByTheDeleter` is empty; `_person.Hash()` equals `before`.
        // Fails before E5.3 g: "Expected the deleter's unlink recorder not to be invoked …".
        throw new NotImplementedException("s554-rootless");
        // QWEN-FILL-END s554-rootless
    }
}
