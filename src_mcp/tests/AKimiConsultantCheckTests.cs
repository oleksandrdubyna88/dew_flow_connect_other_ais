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
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs one turn as a consultation
/// makes it — in a COPY of its scratch repository, on a ONE-SHOT owned home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>), disposed junction-first
/// whatever ended it. Nothing new in the check itself (decision 1): this proves the path <c>KimiConsultant</c> opens (<c>NeedsASanitisedSnapshot</c>,
/// <c>HomesFolder</c>).
/// </summary>
/// <remarks>
/// The check's own code runs for real (<c>ConsultantCheckMode.AnswerAsync</c>, git for real); the shim is played (<see cref="PlayedKimi"/>) over a
/// fake person (<see cref="KimiFixture"/>) — no turn is paid for and the real <c>kimi</c> is never started. Every folder is a
/// <c>testsToDelete-</c> folder of this test. In the <c>fakecli-env</c> collection because <c>GitScratch.AfterUnlink</c> is a static recorder.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _temp.Dispose();
        _data.Dispose();
        _homes.Dispose();
        _person.Dispose();
    }

    /// <summary>Settings for the caller <c>claude</c> consulting <c>my-kimi</c> (runtime <c>kimi</c>); <paramref name="homesRoot"/> empty is the hand-built, root-less case.</summary>
    private PanelSettings Settings(string homesRoot)
    {
        // QWEN-FILL-BEGIN s55-settings test
        // Return new PanelSettings { Providers = [], DataDir = _data.Path, ReviewerTimeout = TimeSpan.FromSeconds(60), ConsultEnabled = true,
        //   ConsultantHomesRoot = homesRoot,
        //   Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") } }.
        // (ConsultantChoice's first positional parameter is the vendor id; a row with no ExecutablePath is what AKimiConsultationScenarioTests.OnKimi uses.)
        throw new NotImplementedException("s55-settings");
        // QWEN-FILL-END s55-settings
    }

    /// <summary>Runs <c>--check-consultant --caller claude</c> through <c>ConsultantCheckMode.AnswerAsync</c> with <paramref name="played"/> as the launcher.</summary>
    private async Task<(int Code, JsonElement Said, string Err)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s55-check test
        // var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], played, _temp.Path, _warned.Add, Noticing.None, ct);
        // return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);   (the same helper as ConsultantCheckTests.Check)
        throw new NotImplementedException("s55-check");
        // QWEN-FILL-END s55-check
    }

    /// <summary>A played Kimi whose answer reads the marker out of the CHECK.md of the cwd it was given — the check's own question — and says it cannot read the canary.</summary>
    private static PlayedKimi PlayedThatReadsTheMarker(IProcessLauncher inner, int exit = 0)
    {
        // QWEN-FILL-BEGIN s55-played test
        // Return new PlayedKimi(inner) { Turn = (_, request) => ... } whose Turn reads File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")),
        // takes its last space-separated word without the trailing '.' as the marker (ConsultantCheckTests's PlayedVibe.AnswerFor does `Trim().Split(' ')[^1].TrimEnd('.')`),
        // and answers PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 10, $"marker: {marker}{Environment.NewLine}canary: CANNOT").
        // When exit != 0 the turn FAILS instead: return new ProcessResult(exit, KimiShimScripts.Report(AcpEnd.AgentFailed, string.Empty, "the agent failed", tokens: false), "the agent failed", TimedOut: false).
        throw new NotImplementedException("s55-played");
        // QWEN-FILL-END s55-played
    }

    /// <summary>The shim's argument after <paramref name="flag"/> (<c>--cwd</c>, <c>--home</c>) of the one launch the played Kimi saw.</summary>
    private static string ArgumentAfter(PlayedKimi played, string flag)
    {
        // QWEN-FILL-BEGIN s55-arg test
        // var arguments = played.Shims.Should().ContainSingle().Subject.Request.Arguments.ToList(); return arguments[arguments.IndexOf(flag) + 1];
        throw new NotImplementedException("s55-arg");
        // QWEN-FILL-END s55-arg
    }

    /// <summary>S5.5.1: the check passes in a copy, on a one-shot home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>, and leaves the scratch repository and its canary as they were.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551 test
        // Arrange: using var stood = KimiStandIn.Of(_person); var played = PlayedThatReadsTheMarker(new ProcessLauncher()); var settings = Settings(_homes.Path).
        // Act: var (code, said, _) = await CheckAsync(settings, played, TestContext.Current.CancellationToken).
        // Assert: code 0; said.GetProperty("state").GetString() is "answered" (and "markerRead" true, as ConsultantCheckTests); ArgumentAfter(played, "--cwd") contains
        //   Path.Combine("own", "tree") (a copy in the check's own scratch, never the repository); ArgumentAfter(played, "--home") matches
        //   ^<_homes.Path>[/\\]coai-vh[/\\][0-9a-f]{8}[/\\]kh-[0-9a-f]{10}$ (Regex.Escape the root; VibeHomes.OneShotFolder is "coai-vh");
        //   Directory.EnumerateDirectories(_temp.Path, "coai-check-*") is empty; the check's own invariant (the scratch repository unchanged, the canary untouched)
        //   is already in the "state" and the "canary" fields the check reports - assert the reported canary reading is not "read".
        // Why red before E5.4: the check failed with "the vendor 'my-kimi' runs on 'kimi', which cannot hold a consultation in this build".
        throw new NotImplementedException("s551");
        // QWEN-FILL-END s551
    }

    /// <summary>S5.5.2: the check ends (passed; and a second arm where the turn fails exit 70) — the one-shot folder is gone, the deleter met no link, the person's folder is whole.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(70)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(int turnExit)
    {
        _ = turnExit; // read by the body below
        // QWEN-FILL-BEGIN s552 test
        // Arrange: using var stood = KimiStandIn.Of(_person); var before = _person.Hash(); GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        //   var played = PlayedThatReadsTheMarker(new ProcessLauncher(), turnExit); settings = Settings(_homes.Path).
        // Act: await CheckAsync(settings, played, TestContext.Current.CancellationToken) (a failed turn is a reported state, not an exception; do not assert code for exit 70).
        // Assert: Directory.Exists(Path.Combine(_homes.Path, VibeHomes.OneShotFolder, <the 8 hex folder>)) is false - read the 8-hex folder name off ArgumentAfter(played, "--home")
        //   (its parent) BEFORE asserting, and also assert the coai-vh folder holds no entry (Directory.EnumerateFileSystemEntries(Path.Combine(_homes.Path, VibeHomes.OneShotFolder)) empty
        //   when it exists); _unlinkedByTheDeleter is empty; _person.Hash() equals before (bytes and ReadOnly bit of the fake person's folder).
        // Why red before E5.3 g: "Expected the deleter's unlink recorder not to be invoked".
        throw new NotImplementedException("s552");
        // QWEN-FILL-END s552
    }

    /// <summary>S5.5.3: the played shim blocks, the token is cancelled — the record says cancelled, and the home is gone junction-first (pins the check's <c>finally</c>).</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553 test
        // Arrange: using var stood = KimiStandIn.Of(_person); using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        //   GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add; var played = new PlayedKimi(new ProcessLauncher()) { During = async (_, ct) => { await cts.CancelAsync(); ct.ThrowIfCancellationRequested(); } };
        //   (PlayedKimi.During is Func<ProcessRequest, CancellationToken, Task>, run while the shim is alive.)
        // Act: var (code, said, _) = await CheckAsync(Settings(_homes.Path), played, cts.Token) - if the check lets OperationCanceledException out, catch it with
        //   `var running = () => CheckAsync(...); await running.Should().ThrowAsync<OperationCanceledException>();` and then read the state file instead:
        //   Path.Combine(_data.Path, "consultations", "health", "claude.check.json") whose "state" is ConsultCheckStates.Cancelled when such a constant exists (grep ConsultCheckStates).
        // Assert: the record (the reply's "state" or the state file) says cancelled; the one-shot folder under Path.Combine(_homes.Path, VibeHomes.OneShotFolder) holds no entry;
        //   _unlinkedByTheDeleter is empty.
        throw new NotImplementedException("s553");
        // QWEN-FILL-END s553
    }

    /// <summary>S5.5.4: settings built by hand have no homes root — the home stays in the check's scratch (<c>&lt;scratch&gt;\own\kh-…</c>) and goes with it, junction-first.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554 test
        // Arrange: using var stood = KimiStandIn.Of(_person); GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add; var before = _person.Hash();
        //   var played = PlayedThatReadsTheMarker(new ProcessLauncher()); var settings = Settings(string.Empty).
        // Act: await CheckAsync(settings, played, TestContext.Current.CancellationToken).
        // Assert: ArgumentAfter(played, "--home") contains Path.Combine("own", "kh-") (under <scratch>\own); after the check the home's parent folder (the check's scratch, <_temp>/coai-check-*)
        //   no longer exists: Directory.EnumerateDirectories(_temp.Path, "coai-check-*") empty; Directory.Exists(<the --home value>) false; _unlinkedByTheDeleter empty; _person.Hash() equals before.
        // Why red before E5.3 g: "Expected the deleter's unlink recorder not to be invoked".
        throw new NotImplementedException("s554");
        // QWEN-FILL-END s554
    }
}
