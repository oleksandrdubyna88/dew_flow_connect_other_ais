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
/// <c>--check-consultant</c> for a Kimi row (S5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): one paid turn as a consultation makes it, in a COPY
/// of the check's scratch repository, on a ONE-SHOT owned home under <c>coai-vh\&lt;8&gt;</c>, disposed junction-first whatever ended the check.
/// Only the shim is played (<see cref="PlayedKimi"/>), over a fake person (<see cref="KimiFixture"/>); the launch control in front of it is the real one.
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly List<string> _warned = [];

    public void Dispose()
    {
        _data.Dispose();
        _temp.Dispose();
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

    /// <summary>The shim answers with the marker it reads in its own working folder, which is the copy the check made.</summary>
    private static PlayedKimi PlayedAsTheCheck(IProcessLauncher inner) => new(inner)
    {
        Turn = (_, request) =>
        {
            var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

            return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
        },
    };

    /// <summary>Runs the check; <paramref name="recorded"/> collects every junction the deleter unlinks (S5.5.2: junction first, never entered).</summary>
    private async Task<(int Code, JsonElement Said)> Check(PanelSettings settings, IProcessLauncher launcher, List<string>? recorded = null, CancellationToken? cancel = null)
    {
        GitScratch.AfterUnlink = recorded is null ? null : (Action<string>)recorded.Add;
        try
        {
            var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
                settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, cancel ?? TestContext.Current.CancellationToken);

            return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>The seams a Kimi launch needs for a fake person on the measured host (the same stand-in the consult scenarios use).</summary>
    private static IDisposable[] StandIn(KimiFixture person) =>
    [
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))),
    ];

    /// <summary>S5.5.1: the check passes on a one-shot home, and the copy is the working folder the shim is given.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var person = new KimiFixture();
        using var homes = TempDir.For("testsToDelete-kimi-check-");
        var played = PlayedAsTheCheck(new ProcessLauncher());
        var stood = StandIn(person);
        try
        {
            var (code, said) = await Check(Settings(homes), played);

            code.Should().Be(0, said.ToString());
            Text(said, "state").Should().Be("answered", said.ToString());
            var turn = played.Shims.Should().ContainSingle().Subject;
            turn.Request.Arguments.Should().Contain("--cwd");
            var cwd = turn.Request.Arguments[Array.IndexOf(turn.Request.Arguments.ToArray(), "--cwd") + 1];
            cwd.Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
            var home = turn.Request.Arguments[Array.IndexOf(turn.Request.Arguments.ToArray(), "--home") + 1];
            home.Should().StartWith(Path.Combine(homes, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar,
                "a one-shot home under the homes root (E5.3 g)");
            Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$", "the home is kh- and ten hex digits (E5.4 decision 2)");
        }
        finally
        {
            foreach (var scope in stood.Reverse())
            {
                scope.Dispose();
            }
        }
    }

    /// <summary>S5.5.2: when the check ends, its one-shot home is gone junction-first, and the fake person's folder is whole.</summary>
    [Fact]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst()
    {
        using var person = new KimiFixture();
        using var homes = TempDir.For("testsToDelete-kimi-check-gone-");
        var played = PlayedAsTheCheck(new ProcessLauncher());
        var stood = StandIn(person);
        try
        {
            var before = person.Hash();
            var recorded = new List<string>();
            var (code, said) = await Check(Settings(homes), played, recorded);

            code.Should().Be(0, said.ToString());
            Directory.EnumerateDirectories(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home's folder goes when the check ends");
            recorded.Should().BeEmpty("the home's junction is unlinked by its own name, so the deleter never met it");
            person.Hash().Should().Equal(before, "the fake person's folder is whole: bytes and attributes");
        }
        finally
        {
            foreach (var scope in stood.Reverse())
            {
                scope.Dispose();
            }
        }
    }

    /// <summary>S5.5.3: a check cancelled mid-turn still disposes its one-shot home, junction first.</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var person = new KimiFixture();
        using var homes = TempDir.For("testsToDelete-kimi-check-cancel-");
        using var stopping = new CancellationTokenSource();
        // The turn starts, the token is cancelled while it runs, and the shim's wait ends with that cancellation.
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = (_, ct) =>
            {
                stopping.Cancel();

                return Task.Delay(Timeout.Infinite, ct);
            },
        };
        var stood = StandIn(person);
        try
        {
            var before = person.Hash();
            var recorded = new List<string>();
            await Check(Settings(homes), played, recorded, stopping.Token);

            Directory.EnumerateDirectories(Path.Combine(homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home goes even when the turn was cancelled");
            recorded.Should().BeEmpty("the junction is unlinked by its own name, so the deleter never met it");
            person.Hash().Should().Equal(before, "the fake person's folder is whole");
        }
        finally
        {
            foreach (var scope in stood.Reverse())
            {
                scope.Dispose();
            }
        }
    }

    /// <summary>S5.5.4: with no homes root, the home stays in the check's own scratch, and goes with it, junction first.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var person = new KimiFixture();
        var played = PlayedAsTheCheck(new ProcessLauncher());
        var stood = StandIn(person);
        try
        {
            var before = person.Hash();
            var recorded = new List<string>();
            var settings = Settings(string.Empty);
            var (code, said) = await Check(settings, played, recorded);

            code.Should().Be(0, said.ToString());
            var turn = played.Shims.Should().ContainSingle().Subject;
            var home = turn.Request.Arguments[Array.IndexOf(turn.Request.Arguments.ToArray(), "--home") + 1];
            home.Should().Contain(Path.Combine("own", "kh-"), "a rootless check keeps its home in its own scratch (S5.5.4)");
            Directory.Exists(home).Should().BeFalse("the scratch goes with the check, and so does the home");
            recorded.Should().BeEmpty("the junction is unlinked by its own name, so the deleter never met it");
            person.Hash().Should().Equal(before, "the fake person's folder is whole");
        }
        finally
        {
            foreach (var scope in stood.Reverse())
            {
                scope.Dispose();
            }
        }
    }
}
