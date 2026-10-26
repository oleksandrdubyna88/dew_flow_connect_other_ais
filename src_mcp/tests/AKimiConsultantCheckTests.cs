using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// <c>coai-mcp --check-consultant</c> for Kimi (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one played Kimi turn, as a
/// consultation makes it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home under <c>coai-vh\&lt;8&gt;</c>, disposed
/// junction-first whatever ended the check. The real launch control runs over a fake person's sign-in (<see cref="KimiFixture"/>) and a real gate
/// file; only the shim is played (<see cref="PlayedKimi"/>), so no turn is paid for and the real <c>kimi</c> is never started.
/// </summary>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly List<string> _warned = [];

    public void Dispose()
    {
        _homes.Dispose();
        _temp.Dispose();
        _data.Dispose();
        _person.Dispose();
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

    /// <summary>The fake person's sign-in, the measured host and the gate's folder, stood in for this async flow (the E5.4 service arms' shape).</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_data.Path, "kimi-locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data.Path,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi"),
        },
    };

    private async Task<(int Code, JsonElement Said)> Check(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp.Path, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    /// <summary>The check's marker, read from the COPY the turn was given — the last word of its one line, as the Vibe check reads it.</summary>
    private static string MarkerAnswer(ProcessRequest request)
    {
        var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');

        return $"marker: {marker}{Environment.NewLine}canary: CANNOT";
    }

    private static PlayedKimi Answering() => new(new ProcessLauncher())
    {
        Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 1000, 10, MarkerAnswer(request)),
    };

    /// <summary>The value after a flag on one launch's argv.</summary>
    private static string Argument(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    /// <summary>Every file of the fake person's folder, by its bytes and attributes — the person's own folder must read the same after the check.</summary>
    private Dictionary<string, string> PersonsFiles() => Directory.EnumerateFiles(_person.Folder, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) + File.GetAttributes(path));

    private string OneShotFolder => Path.Combine(_homes.Path, VibeHomes.OneShotFolder);

    /// <summary>S5.5.1: the check runs in a copy, on a one-shot home under the homes root (J3), and the scratch repository is not touched.</summary>
    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var stand = StandIn();
        var played = Answering();

        var (code, said) = await Check(Settings(_homes.Path), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var request = played.Shims.Should().ContainSingle().Subject.Request;
        Argument(request, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = Argument(request, "--home");
        home.Should().StartWith(OneShotFolder + Path.DirectorySeparatorChar, "the home is under the homes root's coai-vh (J3)");
        Regex.IsMatch(Path.GetFileName(home), "^kh-[0-9a-f]{10}$").Should().BeTrue($"the home is kh-<10 hex>, but was {home}");
        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("the home's folder goes when the check ends");
    }

    /// <summary>S5.5.2: after the check the home is gone junction-first (the deleter never meets the junction), and the person's folder is intact — on an answer and on a turn that loses its session.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool turnFails)
    {
        using var stand = StandIn();
        var unlinkedByTheDeleter = new List<string>();
        var before = PersonsFiles();
        GitScratch.AfterUnlink = unlinkedByTheDeleter.Add;
        try
        {
            var played = turnFails
                ? new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) }
                : Answering();

            var (code, said) = await Check(Settings(_homes.Path), played, TestContext.Current.CancellationToken);

            if (!turnFails)
            {
                code.Should().Be(0);
                said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
            }
            else
            {
                said.GetProperty("state").GetString().Should().NotBe("answered", said.ToString());
            }

            Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("coai-vh\\<8> goes with the check");
            unlinkedByTheDeleter.Should().BeEmpty("the deleter never met the junction: KimiHome unlinked it first");
            PersonsFiles().Should().BeEquivalentTo(before, "the fake person's folder is intact, bytes and attributes");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }
    }

    /// <summary>S5.5.3: a check cancelled while its turn runs still disposes its home junction-first.</summary>
    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        using var stand = StandIn();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        try
        {
            var (_, said) = await Check(Settings(_homes.Path), played, caller.Token);

            said.GetProperty("failureKind").GetString().Should().Be("cancelled", said.ToString());
        }
        catch (OperationCanceledException)
        {
            // A cancelled check may end by throwing instead of answering with its record; either way the home must be gone.
        }

        Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("the home is disposed in finally, whatever ended the check");
    }

    /// <summary>S5.5.4: settings built by hand have no homes root, so the home stays in the check's own scratch, and it is disposed junction-first.</summary>
    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var stand = StandIn();
        var unlinkedByTheDeleter = new List<string>();
        GitScratch.AfterUnlink = unlinkedByTheDeleter.Add;
        try
        {
            var played = Answering();

            var (code, _) = await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

            code.Should().Be(0);
            var home = Argument(played.Shims.Should().ContainSingle().Subject.Request, "--home");
            home.Should().Contain(Path.Combine("own", "kh-"), "with no homes root the home is kept under the check's own scratch");
            Directory.EnumerateDirectories(_temp.Path, "coai-check-*").Should().BeEmpty("the scratch goes with the check");
            unlinkedByTheDeleter.Should().BeEmpty("the deleter never met the junction");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }
    }
}
