using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
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
/// A Kimi question-consultant row on a <c>disk</c> grant (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, decision 5 and S5.6.7): the row is told
/// the COPIES' paths and never the granted root, and after an answer, a refusal before the launch and a cancellation in flight, its one-shot folder under
/// <c>coai-vh</c> is gone, the deleter never met the junction, and the fake person's folder is whole.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the Kimi shim played in process (<see cref="PlayedKimi"/>) over a fake
/// person (<see cref="KimiFixture"/>). Windows only where a launch must start (D18); the production code is the generic question-row launch, unchanged.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinked = [];
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-qvh-");

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_homes.Path).FullName;

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
        GitScratch.AfterUnlink = _unlinked.Add;
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _homes.Dispose();
        _dir.Dispose();
        _person.Dispose();

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

    /// <summary>
    /// The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, so the values reach the test's own flow
    /// and every service call it awaits.
    /// </summary>
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

    private static Func<ProcessRequest, CancellationToken, Task> CancelDuringTurn(CancellationTokenSource caller) => async (_, ct) =>
    {
        await caller.CancelAsync();
        ct.ThrowIfCancellationRequested();
    };

    /// <summary>The row's one-shot folder under <c>coai-vh</c> is empty — or was never made, which is empty too.</summary>
    private static void TheOneShotFolderIsEmpty(string homes)
    {
        var oneShot = Path.Combine(homes, VibeHomes.OneShotFolder);
        var entries = Directory.Exists(oneShot) ? Directory.EnumerateFileSystemEntries(oneShot).ToList() : [];

        entries.Should().BeEmpty("Expected Directory.Exists(\"…\\coai-vh\\<8>\") to be False");
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");
        using var scope = StandIn(_person);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher) { During = ending == "cancelled" ? CancelDuringTurn(caller) : static (_, _) => Task.CompletedTask };
        if (ending == "refused")
        {
            File.WriteAllText(_person.PersonsConfig, KimiFixture.OwnerConfig().Replace(KimiFixture.SlotKey, "oauth/kimi-code-env-y"));
        }

        var before = _person.Hash();
        var homes = Homes;

        await AskTheRowAsync(ending, played, ServiceFor(played, homes, Path.Combine(Repo, "src")), homes, caller.Token);

        TheOneShotFolderIsEmpty(homes);
        _unlinked.Should().BeEmpty("the deleter never met the junction");
        _person.Hash().Should().Equal(before, "the fake person's folder is whole");
    }

    /// <summary>The three endings: an answer names the copy and never the root; a refusal starts nothing; a cancellation ends the ask.</summary>
    private async Task AskTheRowAsync(string ending, PlayedKimi played, PanelService service, string homes, CancellationToken ct)
    {
        if (ending == "cancelled")
        {
            var asking = async () => await AskAsync(service, ct);

            await asking.Should().ThrowAsync<OperationCanceledException>();

            return;
        }

        var reply = await AskAsync(service, ct);
        var row = TheRow(reply);
        if (ending == "refused")
        {
            row.GetProperty("status").GetString().Should().Be(RowOutcomes.Failed, reply.ToString());
            played.Shims.Should().BeEmpty();

            return;
        }

        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
        var prompt = played.Shims.Should().ContainSingle().Subject.Request.StdIn;
        prompt.Should().Contain(Path.Combine(homes, VibeHomes.OneShotFolder), "the prompt names the copy, under the row's folder");
        prompt.Should().NotContain(Path.Combine(Repo, "src"), "never the granted root, which the row cannot read");
    }
}
