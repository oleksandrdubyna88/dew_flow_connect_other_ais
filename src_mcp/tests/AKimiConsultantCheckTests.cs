using System.Text.Json;
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
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): the check runs one turn as a
/// consultation makes it — in a COPY of its scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c> (or under its own
/// scratch when no root is set) — and the home is disposed junction-first whatever ended the check: an answer, a failed turn, a cancellation.
/// </summary>
/// <remarks>
/// <para>The whole mode (<c>ConsultantCheckMode.AnswerAsync</c>), git for real, the shim played in process (<see cref="PlayedKimi"/>) over a fake
/// person's sign-in (<see cref="KimiFixture"/>; the person's folder lives outside the homes root the check deletes). No test starts a real
/// <c>kimi</c>. Every folder a test makes is named <c>testsToDelete-…</c> and removed in <see cref="Dispose"/>.</para>
/// <para>"The deleter's unlink recorder" is <c>GitScratch.AfterUnlink</c>: a Kimi home disposed junction-first never lets the generic deleter
/// meet its junction, so the recorder must stay empty.</para>
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("testsToDelete-kimi-check-temp-").FullName;
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _homes.Dispose();
        _person.Dispose();
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

    /// <summary>The panel's settings for a Kimi row <c>my-kimi</c> answering the <c>claude</c> caller; <paramref name="homesRoot"/> empty is a root-less check.</summary>
    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice> { [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi") },
    };

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    /// <summary>The person's folder, whole: the same names, bytes and read-only bits as <paramref name="before"/>.</summary>
    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    /// <summary>The value after <c>--home</c> on a shim request.</summary>
    private static string HomeOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--home") + 1];

    /// <summary>The check, in process: <c>--check-consultant --caller claude</c> over the given launcher, with the fake person's sign-in and the measured host standing in.</summary>
    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        using var _ = WindowsStandIn.Begin();
        using var _lock = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var _seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));

        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default);
    }

    /// <summary>What the played agent answers: the marker read from the copy's CHECK.md, and the canary it was told not to read.</summary>
    private static ProcessResult AnswersFromTheCopy(ProcessRequest request)
    {
        var marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');
        return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswersFromTheCopy(request) };
        RecordingTheDeleter();
        var before = _person.Hash();

        var (code, said) = await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");
        said.GetProperty("canary").GetString().Should().Be("not-attempted", "the played agent said CANNOT, and the canary outside the copy was untouched");

        var request = played.Shims.Should().ContainSingle().Subject.Request;
        request.WorkingDirectory.Should().Contain(Path.Combine("own", "tree"), "a copy in the check's own scratch, never the repository");

        var home = HomeOf(request);
        home.Should().StartWith(Path.Combine(_homes, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar);
        Path.GetFileName(home).Should().StartWith("kh-").And.HaveLength(13);

        request.Arguments.Should().NotContain("--session");
        ThePersonsFolderIsWhole(before);
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        RecordingTheDeleter();
        var before = _person.Hash();

        var played = theTurnFails
            ? new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request)) }
            : new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswersFromTheCopy(request) };

        await CheckAsync(Settings(_homes), played, TestContext.Current.CancellationToken);

        played.Shims.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder))
            .Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        RecordingTheDeleter();
        var before = _person.Hash();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };

        var check = CheckAsync(Settings(_homes), played, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        try
        {
            var (_, said) = await check;
            said.GetProperty("state").GetString().Should().NotBe("answered", said.ToString());
            said.ToString().Should().ContainEquivalentOf("cancel");
        }
        catch (OperationCanceledException)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the home's folder goes when a cancelled check ends");
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        RecordingTheDeleter();
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher()) { Turn = (_, request) => AnswersFromTheCopy(request) };

        await CheckAsync(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        var request = played.Shims.Should().ContainSingle().Subject.Request;
        HomeOf(request).Should().Contain(Path.Combine("own", "kh-"), "a root-less check keeps its Kimi home under its own scratch");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch is gone after the check");
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }
}
