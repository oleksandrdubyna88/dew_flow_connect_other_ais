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
/// <c>--check-consultant</c> for a Kimi consultant (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation
/// makes it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>, disposed junction-first
/// whatever ended the check. The check has no Kimi branch of its own (decision 1): it gives a copy to a runtime that asks and a one-shot home to a
/// runtime that declares a <c>HomesFolder</c>, which <see cref="KimiConsultant"/> does.
/// </summary>
/// <remarks>
/// The real check, the real git, the real launch control over a fake person (<see cref="KimiFixture"/>) and a real gate file — only the shim is played
/// (<see cref="PlayedKimi"/>), so no turn is paid for and the real <c>kimi</c> is never started. The adapter is built by NAME inside the check, so the
/// fake person and the measured host are stood in per async flow (<see cref="KimiSeams.Using"/>). Every folder a test makes is a
/// <c>testsToDelete-</c> folder of its own; the person's folder lives outside the homes root the check deletes.
/// </remarks>
[Collection("fakecli-env")]
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
        _homes.Dispose();
        _temp.Dispose();
        _data.Dispose();
        _person.Dispose();
    }

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, so the values reach every call the test awaits.</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    /// <summary>A played Kimi that reads <c>CHECK.md</c> in ITS working folder — what a Kimi in the copy would — and cannot read the canary.</summary>
    private static PlayedKimi Reading(IProcessLauncher inner) => new(inner)
    {
        Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.Minted, 1200, 34, MarkerRead(request)),
    };

    private static string MarkerRead(ProcessRequest request)
    {
        var cwd = request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];
        var word = File.ReadAllText(Path.Combine(cwd, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

        return $"marker: {word}{Environment.NewLine}canary: CANNOT";
    }

    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string After(IReadOnlyList<string> arguments, string flag) => arguments[arguments.ToList().IndexOf(flag) + 1];

    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    private string OneShotFolder => Path.Combine(_homes, VibeHomes.OneShotFolder);

    private void NothingIsLeftOfTheCheck()
    {
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the check's scratch goes with the check");
        if (Directory.Exists(OneShotFolder))
        {
            Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");
        }
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var standIn = StandIn();
        var played = Reading(new ProcessLauncher());

        var (code, said) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", $"the check of my-kimi must pass: {said}");
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        After(shim.Arguments, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = After(shim.Arguments, "--home");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(OneShotFolder, "D1: a one-shot folder <root>\\coai-vh\\<8>, never under TEMP");
        Path.GetFileName(Path.GetDirectoryName(home))!.Should().HaveLength(8);
        said.GetProperty("canary").GetString().Should().NotBe("read", "the canary outside the scratch repository stayed untouched");
        NothingIsLeftOfTheCheck();
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        using var standIn = StandIn();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => theTurnFails ? PlayedKimi.Lost(PlayedKimi.Minted) : PlayedKimi.Answered(PlayedKimi.Minted, 1200, 34, MarkerRead(request)),
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        var (_, said) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(theTurnFails ? "failed" : "answered", said.ToString());
        played.Shims.Should().ContainSingle("the check made its one turn");
        Directory.Exists(OneShotFolder).Should().BeTrue("the folder of the one-shot homes is the check's root, kept");
        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("coai-vh\\<8> is gone");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var standIn = StandIn();
        using var cancelling = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        var checking = CheckAsync(Settings(_homes), played, cancelling.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await cancelling.CancelAsync();
        var (_, said) = await checking;

        Text(said, "state").Should().NotBe("answered", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled", "the record says the check was cancelled");
        NothingIsLeftOfTheCheck();
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder is whole");
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var standIn = StandIn();
        var homeSeen = string.Empty;
        var scratchHadTheHome = false;
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) =>
            {
                homeSeen = After(request.Arguments, "--home");
                scratchHadTheHome = Directory.Exists(homeSeen);

                return PlayedKimi.Answered(PlayedKimi.Minted, 1200, 34, MarkerRead(request));
            },
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        var (_, said) = await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        homeSeen.Should().Contain(Path.Combine("own", "kh-"), "without a homes root the home stays in the check's own scratch: <scratch>\\own\\kh-…");
        Path.GetFileName(homeSeen).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        scratchHadTheHome.Should().BeTrue("the launch control had made the home in the scratch by the time the shim ran");
        NothingIsLeftOfTheCheck();
        Directory.Exists(homeSeen).Should().BeFalse("the scratch, home included, is gone");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder is whole");
        _warned.Where(said => said.Contains("kept", StringComparison.Ordinal)).Should().BeEmpty("no live tree: nothing was kept");
    }
}
