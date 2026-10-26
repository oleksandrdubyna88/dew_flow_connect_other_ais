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

        private static Scopes StandIn(KimiFixture person) => new(
            WindowsStandIn.Begin(),
            KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
            KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, [])))
        );
        // QWEN-FILL-END s55-standin
    }

    private PanelSettings Settings(string homesRoot)
    {
        // QWEN-FILL-BEGIN s55-settings test
        return new PanelSettings
        {
            Providers = [],
            DataDir = _data.Path,
            ReviewerTimeout = TimeSpan.FromSeconds(60),
            ConsultEnabled = true,
            ConsultantHomesRoot = homesRoot,
            Consultants = new Dictionary<string, ConsultantChoice>
            {
                [CallerIdentity.Claude] = new ConsultantChoice("my-kimi", Runtime: "kimi"),
            },
        };
        // QWEN-FILL-END s55-settings
    }

    private Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s55-check test
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);
        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
        // QWEN-FILL-END s55-check
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551 test
        using var stood = StandIn(_person);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (n, request) =>
            {
                var checkMd = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md"));
                var lastWord = checkMd.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last().TrimEnd('.');
                var answer = $"marker: {lastWord}{Environment.NewLine}canary: CANNOT";
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, answer);
            }
        };
        RecordingTheDeleter();
        var settings = Settings(_homes.Path);
        var (code, said) = await CheckAsync(settings, played, TestContext.Current.CancellationToken);
        
        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be(CoaiMcp.Server.ConsultCheckStates.Answered);
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue();
        
        played.Shims.Should().ContainSingle();
        var launch = played.Shims.First();
        var args = launch.Request.Arguments.ToList();
        
        var cwdIndex = args.IndexOf("--cwd");
        cwdIndex.Should().BeGreaterOrEqualTo(0);
        var cwdValue = args[cwdIndex + 1];
        cwdValue.Should().Contain(Path.Combine("own", "tree"));
        
        var homeIndex = args.IndexOf("--home");
        homeIndex.Should().BeGreaterOrEqualTo(0);
        var homeValue = args[homeIndex + 1];
        var expectedHomePrefix = Path.Combine(_homes.Path, VibeHomes.OneShotFolder);
        homeValue.Should().StartWith(expectedHomePrefix);
        var homeName = Path.GetFileName(homeValue);
        homeName.Should().MatchRegex("^kh-[0-9a-f]{10}$");
        
        Directory.EnumerateDirectories(_temp.Path, "coai-check-*").Should().BeEmpty();
        // QWEN-FILL-END s551
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        // QWEN-FILL-BEGIN s552 test
        using var stood = StandIn(_person);
        var played = new PlayedKimi(new ProcessLauncher());
        if (theTurnFails)
        {
            played.Turn = (_, _) => new ProcessResult(70, KimiShimScripts.Report(AcpEnd.SessionLost, string.Empty, "lost", tokens: false, session: PlayedKimi.Minted), string.Empty, TimedOut: false);
        }
        else
        {
            played.Turn = (n, request) =>
            {
                var checkMd = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md"));
                var lastWord = checkMd.Trim().Split(' ')[^1].TrimEnd('.');
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"{lastWord}{Environment.NewLine}canary: CANNOT");
            };
        }

        var before = _person.Hash();
        RecordingTheDeleter();
        var settings = Settings(_homes);
        var ct = TestContext.Current.CancellationToken;
        var (code, said) = await CheckAsync(settings, played, ct);

        var oneShotPath = Path.Combine(_homes, VibeHomes.OneShotFolder);
        Directory.EnumerateFileSystemEntries(oneShotPath).Should().BeEmpty("the one-shot home folder is gone or empty after the check");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder remains unchanged");

        if (theTurnFails)
        {
            said.GetProperty("state").GetString().Should().Be(CoaiMcp.Server.ConsultCheckStates.Failed, "the check state is failed when the turn fails");
        }
        else
        {
            said.GetProperty("state").GetString().Should().Be(CoaiMcp.Server.ConsultCheckStates.Answered, "the check state is answered on success");
        }
        // QWEN-FILL-END s552
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553 test
        using var stood = StandIn(_person);
        var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };
        RecordingTheDeleter();

        var running = CheckAsync(Settings(_homes), played, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cts.CancelAsync();
        var (_, said) = await running;

        said.GetProperty("state").GetString().Should().Be(CoaiMcp.Server.ConsultCheckStates.Failed);
        said.GetProperty("failureKind").GetString().Should().Be(CoaiMcp.Runners.Consultation.ConsultFailureKinds.Cancelled);
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home's folder goes when the check ends");
        TheDeleterNeverMetTheJunction();
        // QWEN-FILL-END s553
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554 test
        using var stood = StandIn(_person);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (n, request) =>
            {
                var checkMd = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md"));
                var marker = checkMd.Split(' ')[^1].TrimEnd('.');
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
            }
        };
        RecordingTheDeleter();
        var settings = Settings(string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (code, said) = await CheckAsync(settings, played, ct);
        code.Should().Be(0, "the check succeeds");
        said.GetProperty("state").GetString().Should().Be(CoaiMcp.Server.ConsultCheckStates.Answered, said.ToString());
        played.Shims.Should().ContainSingle("the shim launches exactly once");
        var launch = played.Shims.Single();
        var homeIndex = launch.Request.Arguments.IndexOf("--home");
        homeIndex.Should().BeGreaterOrEqualTo(0, "--home must be present");
        var homeArg = launch.Request.Arguments[homeIndex + 1];
        homeArg.Should().Contain(Path.Combine("own", "kh-"), "the home is under <scratch>\\own\\kh-<10 hex>");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch is gone after the check");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(_person.Hash(), "the person's folder is unchanged");
        // QWEN-FILL-END s554
    }
}
