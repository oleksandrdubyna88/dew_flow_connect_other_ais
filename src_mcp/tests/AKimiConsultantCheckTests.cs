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
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation makes it —
/// in a COPY of the check's scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;/coai-vh/&lt;8&gt;</c> — and the home disposed junction-first
/// whatever ended the check. The check has no Kimi branch of its own: <c>KimiConsultant</c> asks for a copy and declares a homes folder, and the check
/// already serves both (decision 1).
/// </summary>
/// <remarks>
/// The real check, the real git, the real launch control over a fake person's sign-in (<see cref="KimiFixture"/>); only the shim is played
/// (<see cref="PlayedKimi"/>), so no turn is paid for and no real <c>kimi</c> starts. The fake person lives outside the homes root the check deletes.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-temp-").FullName;
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        foreach (var dir in (string[])[_data, _temp])
        {
            Directory.Delete(dir, recursive: true);
        }

        _homes.Dispose();
        _person.Dispose();
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

    /// <summary>The played agent reads the word CHECK.md holds in the copy it stands in, and says it — as the check asks.</summary>
    private static ProcessResult ReadsTheMarker(ProcessRequest request) =>
        PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 10,
            $"marker: {File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.')}{Environment.NewLine}canary: CANNOT");

    private async Task<(int Code, JsonElement Said)> CheckAsync(PlayedKimi played, string homesRoot, CancellationToken ct)
    {
        using var standIn = WindowsStandIn.Begin();
        using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            Settings(homesRoot), ["--check-consultant", "--caller", "claude"], played, _temp, _ => { }, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => ReadsTheMarker(request) };

        var (code, said) = await CheckAsync(played, _homes, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        var request = played.Shims.Should().ContainSingle().Subject.Request;
        KimiEnvironment.FlagOf(request, "--cwd").Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");
        var home = KimiEnvironment.FlagOf(request, AcpWords.HomeFlag);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(Path.Combine(_homes, VibeHomes.OneShotFolder), "J3: under the short homes root, never under TEMP");
        said.GetProperty("canary").GetString().Should().Be("not-attempted", "the scratch repository's canary is untouched");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => theTurnFails ? PlayedKimi.Lost(PlayedKimi.Minted) : ReadsTheMarker(request),
        };

        var (_, said) = await CheckAsync(played, _homes, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be(theTurnFails ? "failed" : "answered", said.ToString());
        played.Shims.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the one-shot folder goes when the check ends");
        TheDeleterNeverMetTheJunction();
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        var (_, said) = await CheckAsync(played, _homes, caller.Token);

        Text(said, "failureKind").Should().Be("cancelled", said.ToString());
        played.Shims.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home goes in finally, whatever ended the check");
        TheDeleterNeverMetTheJunction();
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => ReadsTheMarker(request) };

        var (_, said) = await CheckAsync(played, string.Empty, TestContext.Current.CancellationToken);

        Text(said, "state").Should().Be("answered", said.ToString());
        var home = KimiEnvironment.FlagOf(played.Shims.Should().ContainSingle().Subject.Request, AcpWords.HomeFlag);
        Path.GetDirectoryName(home).Should().EndWith(ConsultantCheck.OwnName, "no root: the home stays in the check's own scratch folder");
        Directory.Exists(home).Should().BeFalse();
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch goes with the check");
        TheDeleterNeverMetTheJunction();
    }
}
