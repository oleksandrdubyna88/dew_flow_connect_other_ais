using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted
/// roots (the branch's own kit loads from disk, M-K9), the prompt names the copies and never the root, and the row is ONE-SHOT — its home under
/// <c>coai-vh\&lt;8&gt;</c>, junction and all, and its scratch with the copies are gone junction-first on an answer, on a launch refused after
/// the home was made, and on a cancellation mid-turn (decision 5: nothing new in <c>QuestionRowLaunch</c> — E5.3 decision g's disposal). A
/// <c>none</c> row runs the same three endings: its scratch is only the empty <c>cwd</c> folder it stands in (the plan gate of 2026-10-27).
/// </summary>
/// <remarks>
/// <para>The real <see cref="QuestionRowLaunch"/>, the real copies (git runs for real), the real launch control over a fake person's sign-in
/// (<see cref="KimiFixture"/>: a real junction on Windows, a symbolic link elsewhere through <see cref="WindowsStandIn"/>) and a real gate file; only
/// the shim and <c>kimi --version</c> are played (<see cref="PlayedKimi"/>), so the real <c>kimi</c> is never started and no turn is paid for.</para>
/// <para>The adapter is built BY NAME (<c>QuestionResolution.For</c>), so the fake person and the measured host are stood in per async flow
/// (<see cref="KimiSeams.Using"/>). The person's folder, the homes root, the repository and the row's scratch are four separate folders: the person's
/// lives OUTSIDE every tree a disposal deletes.</para>
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-qrow-homes-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Granted => Path.Combine(Repo, "src");

    /// <summary>The row's own folder — made through the launch's seam inside this test's folder, so every ending can be asked about it.</summary>
    private string Scratch => _dir.At("scratch");

    public async ValueTask InitializeAsync()
    {
        foreach (var (path, text) in ((string Path, string Text)[])[("src/Mailer.cs", "void Send() { }\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"), ("docs/notes.md", "not granted\n")])
        {
            var full = Path.Combine([Repo, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, text, TestContext.Current.CancellationToken);
        }

        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, Repo), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _homes.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
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

    /// <summary>The fake person, the measured host and this test's lock root, for this flow's Kimi adapters — set by a SYNCHRONOUS call.</summary>
    private Scopes StandIn() => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiHost.Using(KimiHost.Of(HostKind.Windows)),
        KimiSeams.Using(new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>The two capabilities a Kimi row runs with (E5.6 decision 2) — each with its row id, its prompt and that prompt's title.</summary>
    private static readonly IReadOnlyDictionary<string, (string Id, string Prompt, string Title)> Rows =
        new Dictionary<string, (string Id, string Prompt, string Title)>
        {
            ["disk"] = ("kimi-disk", "question-disk", "Projects on this disk"),
            ["none"] = ("kimi-none", "question-opinion", "The best developer's opinion"),
        };

    private static QuestionRow RowOf(string capability) =>
        new(Rows[capability].Id, "my-kimi", "kimi", KimiModels.Default, string.Empty, string.Empty, string.Empty, Rows[capability].Prompt, Enabled: true);

    private static QuestionRow DiskRow() => RowOf("disk");

    /// <summary>What one ending of the row saw: the copies at the moment the shim started, and whether the home's junction was there to dispose.</summary>
    private sealed class Seen
    {
        public ConcurrentQueue<string> Copies { get; } = new();

        public ConcurrentQueue<bool> JunctionThere { get; } = new();

        /// <summary>The home a launch stood on: the shim's <c>--home</c> is the owned ROOT; <c>kimi --version</c> runs IN <c>&lt;root&gt;\home</c>.</summary>
        public void Look(string ownedRoot) => JunctionThere.Enqueue(OwnedPaths.IsLinkIfThere(Path.Combine(ownedRoot, "home", "credentials")));
    }

    /// <summary>The shim's <c>--home</c> — the owned root under <c>coai-vh\&lt;8&gt;</c>.</summary>
    private static string HomeOf(ProcessRequest shim) => shim.Arguments[shim.Arguments.ToList().IndexOf(Runners.Acp.AcpWords.HomeFlag) + 1];

    /// <summary>The three endings (S5.6.7's Theory), each a played Kimi: an answer, a launch refused AFTER the home was made, a cancellation mid-turn.</summary>
    private static readonly IReadOnlyDictionary<string, Func<Seen, CancellationTokenSource, PlayedKimi>> Endings =
        new Dictionary<string, Func<Seen, CancellationTokenSource, PlayedKimi>>
        {
            ["answered"] = (seen, _) => new PlayedKimi(new ProcessLauncher())
            {
                During = (request, _) =>
                {
                    seen.Look(HomeOf(request));
                    foreach (var copy in Directory.EnumerateDirectories(request.WorkingDirectory))
                    {
                        seen.Copies.Enqueue(copy);
                    }

                    return Task.CompletedTask;
                },
            },
            ["refused before the launch"] = (seen, _) => new PlayedKimi(new ProcessLauncher())
            {
                Version = request =>
                {
                    seen.Look(Path.GetDirectoryName(request.WorkingDirectory)!);

                    return new ProcessResult(1, string.Empty, "kimi: cannot start\r\n", TimedOut: false);
                },
            },
            ["cancelled mid-turn"] = (seen, caller) => new PlayedKimi(new ProcessLauncher())
            {
                During = async (request, ct) =>
                {
                    seen.Look(HomeOf(request));
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                },
            },
        };

    /// <summary>Every ending, for both capabilities a Kimi row runs with: a none row's empty folder goes the same way as a disk row's copies.</summary>
    public static TheoryData<string, string> EndingsAndCapabilities()
    {
        var data = new TheoryData<string, string>();
        foreach (var ending in Endings.Keys)
        {
            data.Add(ending, "disk");
            data.Add(ending, "none");
        }

        return data;
    }

    private QuestionRowLaunch Launch(PlayedKimi played) =>
        new(new ReviewerExecutor(played), new UsageLedger(_dir.At("data")), Logger.None, played, _homes)
        {
            MakeScratch = () => Directory.CreateDirectory(Scratch).FullName,
        };

    private RowLaunchInput Input(string capability = "disk")
    {
        var admitted = QuestionAdmission.Admit(RowOf(capability), new QuestionConsultSettings { Roots = [Granted] }, [], Core.Api.ApiOverrides.None)
            .Should().BeOfType<RowAdmission.Admitted>($"a Kimi {capability} row is admitted, flagged (E5.6 decision 2)").Subject;
        admitted.Plan.Grant.Capability.Spelled().Should().Be(capability, "the row's prompt decides its capability");

        return new RowLaunchInput(
            admitted, "Which helper retries an outbound send?", new ReviewerSettings("my-kimi") { Model = KimiModels.Default, Timeout = TimeSpan.FromSeconds(30) },
            Repo, Directory.CreateDirectory(_dir.At("answers")).FullName, string.Empty, TimeSpan.FromSeconds(30))
        {
            ChangesSoFar = _ => Task.FromResult<IReadOnlyList<Core.Consultation.TreeChange>>([]),
            PromptNaming = copies => "Which helper retries an outbound send? Read these folders: " + string.Join(" ; ", copies.Paths),
        };
    }

    private static QuestionRowRecord Start(string capability) =>
        new(Rows[capability].Id, "my-kimi", KimiModels.Default, "kimi", Rows[capability].Prompt, Rows[capability].Title, capability, "unconfined");

    /// <summary>The row, run to its end — its record, or EMPTY when the caller's cancellation ended it (it propagates, as for every row).</summary>
    private static async Task<string> RunAsync(QuestionRowLaunch launch, RowLaunchInput input, CancellationToken ct, string capability = "disk")
    {
        try
        {
            return (await launch.RunAsync(input, Start(capability), ct)).Status;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }

    /// <summary>What each ending's record says.</summary>
    private static readonly IReadOnlyDictionary<string, string> StatusOf = new Dictionary<string, string>
    {
        ["answered"] = RowOutcomes.Answered,
        ["refused before the launch"] = RowOutcomes.Failed,
        ["cancelled mid-turn"] = string.Empty,
    };

    // ---------- S5.6.7 ----------

    /// <summary>
    /// S5.6.7, for a disk row and — accepted from the plan gate of 2026-10-27 — a none row too: whatever ended the row, its scratch (the copies, or
    /// the empty <c>cwd</c> folder) and its <c>coai-vh\&lt;8&gt;</c> folder are gone, the junction was unlinked by <c>KimiHome</c> before the deleter
    /// reached it, and the person's folder is untouched.
    /// </summary>
    [Theory]
    [MemberData(nameof(EndingsAndCapabilities))]
    public async Task AKimiRow_NoneOrDisk_LeavesNothingBehind(string ending, string capability)
    {
        using var stood = StandIn();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var seen = new Seen();
        var played = Endings[ending](seen, caller);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        var status = await RunAsync(Launch(played), Input(capability), caller.Token, capability);

        status.Should().Be(StatusOf[ending], $"the {capability} row {ending}");
        seen.JunctionThere.Should().ContainSingle().Which.Should().BeTrue("the home's junction was made before this ending, so its disposal had one to unlink");
        TheHomeIsGoneJunctionFirst(played, ending, before);
    }

    /// <summary>S5.6.7's Thens after any ending: the home was <c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>; it, the scratch and the copies are gone, junction-first; the person is whole.</summary>
    private void TheHomeIsGoneJunctionFirst(PlayedKimi played, string ending, IReadOnlyList<string> before)
    {
        // `kimi --version` runs IN <root>\coai-vh\<8>\kh-<10 hex>\home.
        var ownedRoot = Path.GetDirectoryName(played.Versions.Should().ContainSingle().Subject.WorkingDirectory)!;
        var oneShot = Path.GetDirectoryName(ownedRoot)!;
        Path.GetFileName(ownedRoot).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(oneShot).Should().Be(Path.Combine(_homes, VibeHomes.OneShotFolder), "the row's home is <root>\\coai-vh\\<8>\\kh-<10 hex> (J3)");
        Directory.Exists(oneShot).Should().BeFalse($"the row's one-shot folder {oneShot} is gone once it {ending}");
        Directory.Exists(Scratch).Should().BeFalse($"the row's scratch, the copies with it, is gone once it {ending}");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    /// <summary>S5.6.7's prompt: the answered row was told the COPIES' paths — the folders it actually stood among — and never the granted root.</summary>
    [Fact]
    public async Task AKimiDiskRow_IsToldItsCopies_NeverTheRoot()
    {
        using var stood = StandIn();
        using var unused = new CancellationTokenSource();
        var seen = new Seen();
        var played = Endings["answered"](seen, unused);

        var status = await RunAsync(Launch(played), Input(), TestContext.Current.CancellationToken);

        status.Should().Be(RowOutcomes.Answered);
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        shim.WorkingDirectory.Should().Be(Path.Combine(Scratch, "tree"), "a disk row stands among the copies of its roots (D6)");
        seen.Copies.Should().ContainSingle("one copy per granted root");
        shim.StdIn.Should().Contain(seen.Copies.Single(), "the prompt names the copy Kimi stands among").And.NotContain(Granted, "never the original root");
        shim.Arguments.Should().NotContain(argument => argument.Contains(Repo, StringComparison.OrdinalIgnoreCase), "nothing on the argv names the checkout");
    }

    // ---------- the whole flow: admission, the fan-out's prompt, the launch ----------

    /// <summary>
    /// A Kimi disk row through <c>ask_consultants</c> (the service builds the adapter by name): answered, flagged "can read this machine", the prompt
    /// the fan-out composed naming the copy, and nothing left under the homes root.
    /// </summary>
    [Fact]
    public async Task AKimiDiskRow_AnswersThroughTheService_FlaggedUnconfined()
    {
        using var stood = StandIn();
        using var unused = new CancellationTokenSource();
        var seen = new Seen();
        var played = Endings["answered"](seen, unused);
        var service = new PanelService(
            new PanelSettings
            {
                ConsultantHomesRoot = _homes,
                Providers = [],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _dir.At("data"),
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                QuestionConsult = new QuestionConsultSettings { Rows = [DiskRow()], Roots = [Granted], RowBudget = TimeSpan.FromSeconds(30) },
            },
            VaultKeys.None("no vault in tests"), default, played, Logger.None, Noticing.None);

        var reply = JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: TestContext.Current.CancellationToken)).RootElement;

        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());
        var row = answers.EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
        row.GetProperty("flag").GetString().Should().Be("unconfined", "D13: a Kimi row can read this machine (M-K6), and every answer says so");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        var copy = seen.Copies.Should().ContainSingle("the disk row stood among one copy, of its one granted root").Subject;
        shim.StdIn.Should().Contain(copy, "the fan-out's prompt names the copy").And.NotContain(Granted);
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's folder under coai-vh is gone");
    }
}
