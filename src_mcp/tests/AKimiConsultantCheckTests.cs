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
/// <c>coai-mcp --check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a
/// consultation makes it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home (junction and all) under
/// <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>, disposed junction-first whatever ended the check. The check's own invariant (the scratch repository
/// unchanged, the canary untouched) holds.
/// </summary>
/// <remarks>
/// The real check over the real launch control, a fake person's sign-in (<see cref="KimiFixture"/>) and a played shim (<see cref="PlayedKimi"/>) — the
/// real <c>kimi</c> is never started and no turn is paid for. The person's folder lives outside the homes root the check disposes.
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
        KimiHome.AfterUnlink = null;
        _temp.Dispose();
        _data.Dispose();
        _homes.Dispose();
        _person.Dispose();
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    /// <summary>The check's panel settings: a Kimi consultant for the caller <c>claude</c>, the data folder and, unless it is empty, the homes root.</summary>
    private PanelSettings Settings(string homesRoot)
    {
        // QWEN-FILL-BEGIN s555-settings test
        // Return new PanelSettings { Providers = [], DataDir = _data.Path, ReviewerTimeout = TimeSpan.FromSeconds(60), ConsultEnabled = true,
        //   ConsultantHomesRoot = homesRoot, Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") } };
        // (CallerIdentity is in CoaiMcp.Server / CoaiMcp.Core — see the pack; `ConsultantChoice` is `CoaiMcp.Server.ConsultantChoice(string Vendor, string Model = "", string Runtime = "", ...)`.)
        throw new NotImplementedException("s555-settings");
        // QWEN-FILL-END s555-settings
    }

    /// <summary>
    /// Runs <c>--check-consultant --caller claude</c> through <c>ConsultantCheckMode.AnswerAsync</c> with <paramref name="played"/> as the launcher, the
    /// fake person, the measured host and a lock root stood in for THIS async flow; the reply's JSON is returned with the exit code.
    /// </summary>
    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s555-check test
        // using var standIn = WindowsStandIn.Begin(); using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        // using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));
        // var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], played, _temp.Path, _warned.Add, Noticing.None, ct);
        // return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
        // (The same stand-in set as AKimiConsultationScenarioTests.StandIn, minus the service; Noticing is in CoaiMcp.Server.)
        throw new NotImplementedException("s555-check");
        // QWEN-FILL-END s555-check
    }

    /// <summary>The played Kimi that answers the check: it reads CHECK.md in the shim request's <c>--cwd</c> and answers the two lines the check asks for.</summary>
    private PlayedKimi PlayedThatReadsTheMarker()
    {
        // QWEN-FILL-BEGIN s555-played test
        // return new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 10, answer) } where `answer` is
        // $"marker: {word}{Environment.NewLine}canary: CANNOT" and `word` is the LAST space-separated word of CHECK.md in the folder after "--cwd" in request.Arguments
        // (File.ReadAllText(Path.Combine(cwd, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')) — the way ConsultantCheckTests' Vibe test reads it.
        throw new NotImplementedException("s555-played");
        // QWEN-FILL-END s555-played
    }

    /// <summary>The value after <paramref name="flag"/> in a shim request's argument list.</summary>
    private static string After(ProcessRequest request, string flag) =>
        request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    private void TheOneShotFolderIsGone() =>
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");

    /// <summary>S5.5.1: the check passes in a COPY of the scratch repository, on a one-shot home <c>coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-copy test
        // Arrange: var played = PlayedThatReadsTheMarker(); var settings = Settings(_homes.Path); RecordingTheDeleter().
        // Act: var (code, said) = await CheckAsync(settings, played, TestContext.Current.CancellationToken);
        // Assert: code.Should().Be(0); said.GetProperty("state").GetString().Should().Be(ConsultCheckStates.Answered, said.ToString());
        //  said.GetProperty("markerRead").GetBoolean().Should().BeTrue();
        //  var turn = played.Shims.Should().ContainSingle().Subject.Request;
        //  After(turn, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        //  var home = After(turn, AcpWords.HomeFlag); home.Should().StartWith(Path.Combine(_homes.Path, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar);
        //  Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$"); Path.GetFileName(Path.GetDirectoryName(home)!).Should().HaveLength(8) (the <8> folder);
        //  Directory.EnumerateDirectories(_temp.Path, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
        // The scratch repository and its canary unchanged: the "canary" property of `said` is ConsultCheckStates.NotAttempted ("not-attempted") — see ConsultantCheckTests.
        // Before E5.4 the check said: "Expected the check of my-kimi to pass, but it failed: the vendor 'my-kimi' runs on 'kimi', which cannot hold a consultation in this build ...".
        throw new NotImplementedException("s551-copy");
        // QWEN-FILL-END s551-copy
    }

    /// <summary>S5.5.2: after the check — passed, and again with the turn failing (exit 70) — <c>coai-vh\&lt;8&gt;</c> is gone, junction-first, the person's folder whole.</summary>
    [Fact]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst()
    {
        // QWEN-FILL-BEGIN s552-gone test
        // Arrange: var before = _person.Hash(); RecordingTheDeleter(). Two arms in a loop over played shims: PlayedThatReadsTheMarker(), and
        // new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.Minted) } (exit 70, end session_lost).
        // Act: await CheckAsync(Settings(_homes.Path), played, TestContext.Current.CancellationToken) for each.
        // Assert after EACH: TheOneShotFolderIsGone(); TheDeleterNeverMetTheJunction(); _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before").
        // Before E5.3 g: "Expected the deleter's unlink recorder not to be invoked ...".
        throw new NotImplementedException("s552-gone");
        // QWEN-FILL-END s552-gone
    }

    /// <summary>S5.5.3: a check cancelled while the played shim is blocked says cancelled and still disposes its home junction-first.</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancel test
        // Arrange: using var cts = new CancellationTokenSource(); RecordingTheDeleter();
        //  var played = new PlayedKimi(new ProcessLauncher()) { During = async (_, ct) => { cts.Cancel(); await Task.Delay(Timeout.Infinite, ct); } };
        //  (the shim "blocks" until the token the launcher hands it is cancelled; cancel it from inside, once the shim has started).
        // Act: var (_, said) = await CheckAsync(Settings(_homes.Path), played, cts.Token);
        // Assert: said's "state" is not "answered" — the record says cancelled: read how ConsultantCheckTests / ConsultantCheck.cs spell a cancelled check
        //  (`failureKind` / `state`), and assert THAT word; TheOneShotFolderIsGone(); TheDeleterNeverMetTheJunction().
        // Green after E5.3 g (the test pins the `finally`).
        throw new NotImplementedException("s553-cancel");
        // QWEN-FILL-END s553-cancel
    }

    /// <summary>S5.5.4: settings built by hand (no homes root) keep the home in the check's scratch, <c>&lt;scratch&gt;\own\kh-…</c>, and the scratch goes junction-first.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        // Arrange: var played = PlayedThatReadsTheMarker(); var settings = Settings(string.Empty) (an empty ConsultantHomesRoot); RecordingTheDeleter().
        // Act: var (code, said) = await CheckAsync(settings, played, TestContext.Current.CancellationToken).
        // Assert: code.Should().Be(0); var home = After(played.Shims.Should().ContainSingle().Subject.Request, AcpWords.HomeFlag);
        //  home.Should().Contain(Path.Combine("own", "kh-")); Directory.EnumerateDirectories(_temp.Path, "coai-check-*").Should().BeEmpty("the scratch is gone");
        //  Directory.Exists(home).Should().BeFalse(); TheDeleterNeverMetTheJunction().
        // Before E5.3 g: "Expected the deleter's unlink recorder not to be invoked ...".
        throw new NotImplementedException("s554-rootless");
        // QWEN-FILL-END s554-rootless
    }
}
