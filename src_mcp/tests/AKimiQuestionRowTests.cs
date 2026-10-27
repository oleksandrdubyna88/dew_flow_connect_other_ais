using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi disk question row (E5.6 S5.6.7 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): the row reads COPIES of its granted roots and
/// is told the copies' paths, never the roots; whatever ends it — an answer, a refusal before the launch, a cancellation in flight — the
/// row's folder and its <c>coai-vh</c> one-shot folder are gone, junction-first, and the fake person's folder is left whole (E5.3 decision g).
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the Kimi CLI played in process (<see cref="PlayedKimi"/>) over a
/// fake person (<see cref="KimiFixture"/>), in the Windows stand-in (D18) — so the row runs on every host.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");

    private string Repo => _dir.At("repo");

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"));
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _dir.Dispose();

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

    /// <summary>The stand-in every Kimi row runs in: Windows, the fake person's sign-in and its lock root (as <c>AKimiConsultationScenarioTests.StandIn</c>).</summary>
    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private async Task RepositoryAsync(string at, params (string Path, string Text)[] files)
    {
        Directory.CreateDirectory(at);
        foreach (var (path, text) in files)
        {
            var full = Path.Combine([at, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, text, TestContext.Current.CancellationToken);
        }

        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    /// <summary>The service with a homes root: a home-keeping row reserves its one-shot folder there (D1).</summary>
    private PanelService ServiceFor(PlayedKimi kimi, string homesRoot, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = homesRoot,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, PlayedKimi.Shim, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        kimi,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>The caller cancels while the shim is alive: the row ends by its cancellation, not by its answer.</summary>
    private static async Task CancelInFlight(CancellationTokenSource caller, CancellationToken ct)
    {
        await caller.CancelAsync();
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>The row's own folder — the parent of the home its launch was given.</summary>
    private static string RowFolderOf(ProcessRequest request) =>
        Path.GetDirectoryName(KimiEnvironment.FlagOf(request, "--home"))!;

    // ---------- the homes root and the row's one-shot folder (D1) ----------

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var person = new KimiFixture();
        using var standIn = StandIn(person);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var unlinked = new List<string>();
        GitScratch.AfterUnlink = unlinked.Add;
        var played = ending == "cancelled" ? new PlayedKimi(_launcher) { During = async (_, ct) => await CancelInFlight(caller, ct) } : new PlayedKimi(_launcher);
        var granted = Path.Combine(Repo, "src");
        if (ending == "refused")
        {
            File.WriteAllText(person.PersonsConfig, KimiFixture.OwnerConfig().Replace(KimiFixture.SlotKey, "oauth/kimi-code-env-y"));
        }

        var before = person.Hash();
        var service = ServiceFor(played, Homes, granted);

        if (ending == "cancelled")
        {
            Func<Task> asking = () => AskAsync(service, caller.Token);
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await AskAsync(service, caller.Token);

            TheRow(reply).GetProperty("status").GetString().Should().Be(ending == "refused" ? RowOutcomes.Failed : RowOutcomes.Answered, reply.ToString());
        }

        if (ending == "refused")
        {
            played.Shims.Should().BeEmpty("the launch control refused before anything started");
        }
        else
        {
            var request = played.Shims.Should().ContainSingle().Subject.Request;
            request.StdIn.Should().Contain(KimiEnvironment.FlagOf(request, "--cwd"), "the prompt names the copy Kimi can read");
            request.StdIn.Should().NotContain(Path.Combine(Repo, "src"), "never the root, which it cannot read");
        }

        foreach (var launch in played.Shims)
        {
            Directory.Exists(RowFolderOf(launch.Request)).Should().BeFalse("the row's folder is gone once it ended");
            Directory.Exists(KimiEnvironment.FlagOf(launch.Request, "--cwd")).Should().BeFalse("the copies are gone once it ended");
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        unlinked.Should().BeEmpty("the junction is unlinked first, never deleted through");
        person.Hash().Should().Equal(before, "the person's folder is left whole");
    }
}
