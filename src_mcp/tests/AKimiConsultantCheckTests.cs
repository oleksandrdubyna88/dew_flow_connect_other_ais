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
/// <c>coai-mcp --check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation
/// makes it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>, disposed junction-first
/// whatever ended the check. The check's own invariant (the scratch repository unchanged, the canary untouched) holds.
/// </summary>
/// <remarks>
/// <para>No new branch in the check: it already gives a copy to a runtime that asks (<c>NeedsASanitisedSnapshot</c>) and a one-shot home to a runtime that
/// declares a <c>HomesFolder</c>, which <see cref="KimiConsultant"/> does (E5.4 decision 6). What makes it safe for Kimi is E5.3 decision g, so these tests
/// prove the PATH. The shim is played (<see cref="PlayedKimi"/>) over a fake person (<see cref="KimiFixture"/>) — no turn is paid for and the real
/// <c>kimi</c> is never started. Every folder is a <c>testsToDelete-</c> folder of the test's own; the fake person lives OUTSIDE the homes root.</para>
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _data.Dispose();
        _temp.Dispose();
        _homes.Dispose();
        _person.Dispose();
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

    private string OneShotRoot => Path.Combine(_homes, VibeHomes.OneShotFolder);

    /// <summary>The settings of a check whose caller is a Kimi row <c>my-kimi</c>, with the homes root given (empty: a check built by hand, rootless).</summary>
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

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, as the service scenarios do.</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    /// <summary>The check, run through <c>ConsultantCheckMode.AnswerAsync</c> with args <c>--check-consultant --caller claude</c>; the exit code and the JSON it printed.</summary>
    private Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s55-check test
        // var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);
        // return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
        // (make the method `async`; `played` is the IProcessLauncher — PlayedKimi runs git for real through its inner launcher.)
        throw new NotImplementedException("s55-check");
        // QWEN-FILL-END s55-check
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-copy test
        // Arrange: using var stood = StandIn(); var played = new PlayedKimi(new ProcessLauncher()) whose Turn answers the check's marker:
        //   Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20000, 30, answer) where answer follows ConsultantCheckTests'
        //   AVibeCheck test: "marker: <the last word of <request.WorkingDirectory>/CHECK.md, trailing '.' trimmed>" + Environment.NewLine + "canary: CANNOT"
        //   (read the file in the copy: File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md"))). var before = _person.Hash().
        // Act: var (code, said, _) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken).
        // Assert: code Be 0; said.GetProperty("state").GetString() Be "answered" (message said.ToString()); said.GetProperty("markerRead").GetBoolean() BeTrue;
        //   var request = played.Shims.Should().ContainSingle().Subject.Request; --cwd value (the argument after "--cwd") Contain Path.Combine("own", "tree")
        //   and request.WorkingDirectory the same; --home value's parent folder's parent Be OneShotRoot (i.e. <root>\coai-vh\<8>\kh-<10 hex>:
        //   Path.GetDirectoryName(Path.GetDirectoryName(home)) Be OneShotRoot) and Path.GetFileName(home) MatchRegex ^kh-[0-9a-f]{10}$;
        //   the scratch repository and its canary unchanged: said carries no "treeChanged"/"canaryChanged" breach — state "answered" is the proof;
        //   Directory.EnumerateDirectories(_temp, "coai-check-*") BeEmpty. ThePersonsFolderIsWhole(before).
        // Fails before E5.4: "Expected the check of my-kimi to pass, but it failed: the vendor 'my-kimi' runs on 'kimi', which cannot hold a consultation in this build".
        throw new NotImplementedException("s551-copy");
        // QWEN-FILL-END s551-copy
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        _ = theTurnFails; // the body below reads it (it is the arm); drop this line once it does
        // QWEN-FILL-BEGIN s552-gone test
        // Arrange: using var stood = StandIn(); RecordingTheDeleter(); var before = _person.Hash(); the played shim answers as in s551-copy, or — when theTurnFails —
        //   Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) (a played turn with end session_lost and exit 70; any exit-70 result will do).
        // Act: await Check(Settings(_homes), played, ct).
        // Assert: Directory.Exists(OneShotRoot) is false OR Directory.EnumerateFileSystemEntries(OneShotRoot) is empty (the <8> folder is gone, in both arms);
        //   TheDeleterNeverMetTheJunction(); ThePersonsFolderIsWhole(before) (bytes and ReadOnly bits — Hash() carries both);
        //   in the failing arm the check's state is not "answered".
        // Fails before E5.3 g: "Expected the deleter's unlink recorder not to be invoked …".
        throw new NotImplementedException("s552-gone");
        // QWEN-FILL-END s552-gone
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancel test
        // Arrange: using var stood = StandIn(); RecordingTheDeleter(); var before = _person.Hash(); using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        //   var played = new PlayedKimi(new ProcessLauncher()) { During = async (_, ct) => { cts.Cancel(); await Task.Delay(Timeout.Infinite, ct); } }
        //   (the shim blocks until the launcher's token is cancelled).
        // Act: var (_, said, _) = await Check(Settings(_homes), played, cts.Token) — the check's own answer to a cancelled caller is a record, not an exception;
        //   if AnswerAsync lets OperationCanceledException out, catch it with Assert.ThrowsAnyAsync and go on to the asserts.
        // Assert: the record says cancelled (said.GetProperty("state") is "failed" and its failure kind/reason names "cancelled" — print said.ToString() in the message);
        //   OneShotRoot has no entries (or is absent); TheDeleterNeverMetTheJunction(); ThePersonsFolderIsWhole(before).
        // Green after E5.3 g — it pins the `finally` in ConsultantCheck.TurnAsync.
        throw new NotImplementedException("s553-cancel");
        // QWEN-FILL-END s553-cancel
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        // Settings built by hand: Settings(string.Empty) — no homes root, so the home is <scratch>\own\kh-<10 hex> (E5.5 decision 2).
        // Arrange: using var stood = StandIn(); RecordingTheDeleter(); var before = _person.Hash(); a played shim as in s551-copy.
        // Act: await Check(Settings(string.Empty), played, ct). (If AnswerAsync refuses an empty root with exit 74 — ConsultantCheckMode.MissingRoot — see the pack:
        //   then construct the settings the way that method accepts and say so in a comment.)
        // Assert: the shim's --home value has Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(home))) ... precisely: Path.GetFileName(Path.GetDirectoryName(home)) Be "own"
        //   and Path.GetFileName(home) MatchRegex ^kh-[0-9a-f]{10}$; afterwards Directory.EnumerateDirectories(_temp, "coai-check-*") BeEmpty (the scratch is gone);
        //   TheDeleterNeverMetTheJunction(); ThePersonsFolderIsWhole(before).
        // Fails before E5.3 g: "Expected the deleter's unlink recorder not to be invoked …".
        throw new NotImplementedException("s554-rootless");
        // QWEN-FILL-END s554-rootless
    }
}
