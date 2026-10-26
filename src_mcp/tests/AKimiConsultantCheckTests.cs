using System.Text.Json;
using CoaiMcp.Core.Consultation;
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
/// The consultant check on Kimi (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): one turn as a consultation makes it — in a COPY of the
/// check's scratch repository, on a ONE-SHOT home under the homes root's <c>coai-vh\&lt;8&gt;</c>, disposed junction-first whatever ended
/// the check. Nothing here is new in the check (decision 1); these tests prove the path.
/// </summary>
/// <remarks>
/// The shim is played in process (<see cref="PlayedKimi"/>), the person is <see cref="KimiFixture"/>'s fake one (a real junction on Windows,
/// a symbolic link elsewhere through <see cref="WindowsStandIn"/>). The deleter's unlink recorder is static, so the class shares the
/// <c>fakecli-env</c> collection with the other tests that set it.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-");
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
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

    /// <summary>Several scopes, ended in reverse.</summary>
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

    /// <summary>The host, the gate's lock root and the person this flow's Kimi adapters see — a SYNCHRONOUS call, so the values reach every await.</summary>
    private Scopes StandIn() => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>A check of the caller's Kimi row; <paramref name="homesRoot"/> empty is the root-less check of settings built by hand.</summary>
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

    private async Task<(int Code, JsonElement Said)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    /// <summary>The played Kimi reads the marker the check wrote into ITS working folder — the copy — and never the canary.</summary>
    private static PlayedKimi Answering() => new(new ProcessLauncher())
    {
        Turn = static (_, request) => PlayedKimi.Answered(
            PlayedKimi.SessionOf(request), 1200, 40,
            $"marker: {File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT"),
    };

    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first (E5.3 g)");

    private string OneShotRoot => Path.Combine(_homes, VibeHomes.OneShotFolder);

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var standIn = StandIn();
        var played = Answering();

        var (code, said) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        (said.TryGetProperty("confinement", out var confinement) ? confinement.GetString() : string.Empty)
            .Should().BeNullOrEmpty("Kimi's confinement does not depend on its CLI's version (E5.5 decision 3)");
        var shim = played.Shims.Should().ContainSingle().Subject.Request;
        shim.WorkingDirectory.Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        After(shim.Arguments, "--cwd").Should().Be(shim.WorkingDirectory);
        var home = After(shim.Arguments, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().HaveLength(8, "coai-vh\\<8> (J3)");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(OneShotRoot, "D1: under the short homes root, never TEMP");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool answered)
    {
        using var standIn = StandIn();
        var played = answered
            ? Answering()
            : new PlayedKimi(new ProcessLauncher()) { Turn = static (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) };
        var before = _person.Hash();
        RecordingTheDeleter();

        var (code, said) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be(answered ? "answered" : "failed", said.ToString());
        var home = After(played.Shims.Should().ContainSingle().Subject.Request.Arguments, AcpWords.HomeFlag);
        Directory.Exists(Path.GetDirectoryName(home)).Should().BeFalse("coai-vh\\<8> goes when the check ends, whatever ended it");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var standIn = StandIn();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        var (code, said) = await Check(Settings(_homes), played, caller.Token);

        code.Should().Be(0);
        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled");
        var home = After(played.Shims.Should().ContainSingle().Subject.Request.Arguments, AcpWords.HomeFlag);
        Directory.Exists(Path.GetDirectoryName(home)).Should().BeFalse("finally disposes the one-shot home on cancellation too");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before);
    }

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var standIn = StandIn();
        var played = Answering();
        var before = _person.Hash();
        RecordingTheDeleter();

        var (code, said) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        var home = After(played.Shims.Should().ContainSingle().Subject.Request.Arguments, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be(ConsultantCheck.OwnName, "<scratch>\\own\\kh-… (E5.5 decision 2)");
        Path.GetDirectoryName(Path.GetDirectoryName(home))!.Should().StartWith(Path.Combine(_temp, "coai-check-"));
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch goes, its Kimi home first");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before);
    }
}
