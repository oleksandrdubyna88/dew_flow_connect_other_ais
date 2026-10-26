using System.Text.Json;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The consultant check (<c>--check-consultant</c>) on a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1, S5.5.2 and
/// S5.5.4): the check's own scratch repository, its copy, and a ONE-SHOT Kimi home that goes junction-first whatever ended the check. The shim is
/// played (<see cref="PlayedKimi"/>) and git runs for real; the fake person is <see cref="KimiFixture"/>.
/// </summary>
/// <remarks>
/// Every folder this makes is a <c>testsToDelete-</c> folder, the fixture's own, or the check's scratch under the test's temp folder. The E5.5
/// red column named the state before E5.4; with the adapter in place these arms are green on arrival, and their teeth are checked by breaking the
/// deciding line (see the E5.5 notes).
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-kimicheck-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("testsToDelete-kimicheck-temp-").FullName;
    private readonly List<string> _warned = [];

    public void Dispose()
    {
        foreach (var dir in (string[])[_data, _temp])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    /// <summary>A Kimi row on the played shim, with the homes root the test names (empty: the rootless fallback).</summary>
    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("my-kimi", Model: KimiModels.Default, Runtime: "kimi", ExecutablePath: PlayedKimi.Shim),
        },
    };

    /// <summary>The person and the host this check's Kimi adapter sees (the <c>KimiSeams.Using</c> precedent of the service scenarios).</summary>
    private static IDisposable StandIn(KimiFixture person) => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    /// <summary>The played agent answers the check: the marker it reads from the copy's <c>CHECK.md</c>, and the canary it could not read.</summary>
    private static PlayedKimi Answering(IProcessLauncher inner) => new(inner)
    {
        Turn = (_, request) => PlayedKimi.Answered(
            PlayedKimi.SessionOf(request), 19973, 31,
            $"marker: {File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT"),
    };

    private async Task<(int Code, JsonElement Said)> Check(PanelSettings settings, IProcessLauncher launcher)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], launcher, _temp, _warned.Add, Noticing.None, TestContext.Current.CancellationToken);

        return (code, JsonDocument.Parse(stdout).RootElement.Clone());
    }

    /// <summary>The value after <c>--home</c> on the shim's argv.</summary>
    private static string HomeOf(ProcessRequest request) => request.Arguments.SkipWhile(a => a != "--home").Skip(1).First();

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        using var person = new KimiFixture();
        using var homes = TempDir.For("testsToDelete-kimicheck-homes-");
        using var scope = StandIn(person);
        var played = Answering(new ProcessLauncher());

        var (code, said) = await Check(Settings(homes.Path), played);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the check's CHECK.md");
        var turn = played.Shims.Should().ContainSingle().Subject.Request;
        turn.WorkingDirectory.Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the scratch repository (D6)");
        var home = HomeOf(turn);
        home.Should().StartWith(Path.Combine(homes.Path, Runners.Vibe.VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar,
            "E5.3 g: the one-shot home is under the homes root, never under TEMP");
        Path.GetFileName(home).Should().StartWith("kh-");
        Path.GetFileName(Path.GetDirectoryName(home)!).Should().HaveLength(8, "the <8> reservation folder under coai-vh");
    }

    [Fact]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst()
    {
        using var person = new KimiFixture();
        using var homes = TempDir.For("testsToDelete-kimicheck-gone-");
        using var scope = StandIn(person);
        var played = Answering(new ProcessLauncher());
        var before = Bytes(person.PersonsHome);
        var unlinked = new List<string>();

        (int Code, JsonElement Said) result;
        GitScratch.AfterUnlink = unlinked.Add;
        try
        {
            result = await Check(Settings(homes.Path), played);
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        result.Code.Should().Be(0, result.Said.ToString());
        Directory.EnumerateFileSystemEntries(Path.Combine(homes.Path, Runners.Vibe.VibeHomes.OneShotFolder)).Should().BeEmpty(
            "the check disposes its home junction-first when it ends");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the check's scratch goes with the check");
        unlinked.Should().BeEmpty("KimiHome unlinked the junction first, so the deleter never met it");
        Bytes(person.PersonsHome).Should().BeEquivalentTo(before, "the person's own sign-in is never written through the link");
    }

    /// <summary>Every file under a folder, by its path relative to it, with its bytes.</summary>
    private static Dictionary<string, byte[]> Bytes(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file), File.ReadAllBytes);

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        using var person = new KimiFixture();
        using var scope = StandIn(person);
        var played = Answering(new ProcessLauncher());
        var unlinked = new List<string>();

        (int Code, JsonElement Said) result;
        GitScratch.AfterUnlink = unlinked.Add;
        try
        {
            result = await Check(Settings(string.Empty), played);
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        result.Code.Should().Be(0, result.Said.ToString());
        var turn = played.Shims.Should().ContainSingle().Subject.Request;
        HomeOf(turn).Should().Contain(Path.Combine("own", "kh-"), "with no root the home stays in the check's own scratch");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("after the check the scratch, and the home in it, are gone");
        unlinked.Should().BeEmpty("KimiHome unlinked the junction first, so the deleter never met it");
    }
}
