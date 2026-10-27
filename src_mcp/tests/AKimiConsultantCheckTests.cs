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

/// <summary>E5.5 of todo/PLAN_vibe_and_kimi_runtimes_E5.md: the consultant check of a Kimi row — a copy, a one-shot home, disposed junction-first on every ending.</summary>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-check-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public AKimiConsultantCheckTests()
    {
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
    }

    private string Data => Directory.CreateDirectory(_dir.At("data")).FullName;

    private string Temp => Directory.CreateDirectory(_dir.At("temp")).FullName;

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    private string OneShots => Path.Combine(Homes, VibeHomes.OneShotFolder);

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

    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = Data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") },
    };

    private async Task<JsonElement> Check(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], played, Temp, _warned.Add, Noticing.None, ct);
        code.Should().Be(0);
        return JsonDocument.Parse(stdout).RootElement.Clone();
    }

    private static string Text(JsonElement e, string name) => e.GetProperty(name).GetString() ?? string.Empty;

    private static string ArgAfter(ProcessRequest r, string flag) => r.Arguments[r.Arguments.ToList().IndexOf(flag) + 1];

    private void NothingLeftAndJunctionFirst(IReadOnlyList<string> before)
    {
        Directory.EnumerateDirectories(Temp, ConsultCheckScratch.Prefix + "*").Should().BeEmpty("the scratch is deleted at the end");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }

    private static ProcessResult FailedExit70() =>
        new(70, KimiShimScripts.Report(AcpEnd.AgentFailed, string.Empty, "the agent failed", tokens: false), string.Empty, TimedOut: false);

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stood = StandIn();
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher());

        var said = await Check(Settings(Homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(ConsultCheckStates.Answered, said.ToString());
        said.GetProperty("answered").GetBoolean().Should().BeTrue();
        Text(said, "canary").Should().Be("not-attempted", "the canary outside the repository is untouched");
        var request = played.Shims.Should().ContainSingle("one paid turn").Subject.Request;
        var cwd = ArgAfter(request, "--cwd");
        cwd.Should().StartWith(Path.Combine(Temp, ConsultCheckScratch.Prefix))
            .And.Contain(Path.DirectorySeparatorChar + Path.Combine(ConsultantCheck.OwnName, "tree"), "a copy of the scratch repository, never the repository itself (D6)");
        var home = ArgAfter(request, "--home");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().HaveLength(8);
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(OneShots, "the one-shot home under the homes root (J3)");
        if (said.TryGetProperty("confinement", out var confinement))
        {
            var empty = confinement.ValueKind == JsonValueKind.String
                ? confinement.GetString() == string.Empty
                : confinement.ValueKind == JsonValueKind.Array && confinement.GetArrayLength() == 0;
            empty.Should().BeTrue("Decision 3: Kimi's recorded confinement is empty, as Vibe's");
        }

        NothingLeftAndJunctionFirst(before);
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("failed")]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(string ending)
    {
        using var stood = StandIn();
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = ending == "failed" ? static (_, _) => FailedExit70() : static (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31),
        };

        var said = await Check(Settings(Homes), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(ending == "failed" ? "failed" : ConsultCheckStates.Answered, said.ToString());
        Directory.Exists(Path.GetDirectoryName(ArgAfter(played.Shims.Single().Request, "--home"))).Should().BeFalse("coai-vh/<8> is gone");
        (Directory.Exists(OneShots) ? Directory.EnumerateDirectories(OneShots) : []).Should().BeEmpty();
        NothingLeftAndJunctionFirst(before);
    }

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stood = StandIn();
        var before = _person.Hash();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await cancel.CancelAsync();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };

        var said = await Check(Settings(Homes), played, cancel.Token);

        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled");
        Directory.Exists(Path.GetDirectoryName(ArgAfter(played.Shims.Single().Request, "--home"))).Should().BeFalse("coai-vh/<8> is gone");
        (Directory.Exists(OneShots) ? Directory.EnumerateDirectories(OneShots) : []).Should().BeEmpty();
        NothingLeftAndJunctionFirst(before);
    }

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stood = StandIn();
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher());

        var said = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(ConsultCheckStates.Answered, said.ToString());
        var home = ArgAfter(played.Shims.Single().Request, "--home");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "no homes root: the home is kept in the check's scratch, <scratch>/own/kh-…");
        home.Should().StartWith(Path.Combine(Temp, ConsultCheckScratch.Prefix));
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        NothingLeftAndJunctionFirst(before);
    }

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();
    }
}
