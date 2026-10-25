using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads the COPIES of its granted
/// root and is told their paths, never the root's; and whatever ends the row — an answer, a refusal before the launch, a cancellation — its scratch
/// and its one-shot home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c> are gone, junction-first, with the fake person's folder whole.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played (<see cref="PlayedKimi"/>) over a fake person's sign-in
/// (<see cref="KimiFixture"/>): no turn is paid for and no real <c>kimi</c> is started. The fake person lives outside every folder a row deletes.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"));

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

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

    private PanelService Service(PlayedKimi kimi, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = Homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        kimi,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null)
    {
        using var standIn = WindowsStandIn.Begin();
        using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        return JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;
    }

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    private static string After(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    private void NothingOfTheRowIsLeft(ProcessRequest? shim, IReadOnlyList<string> personBefore)
    {
        if (shim is not null)
        {
            Directory.Exists(After(shim, "--cwd")).Should().BeFalse("the row's copy goes with its scratch");
            Directory.Exists(After(shim, AcpWords.HomeFlag)).Should().BeFalse("the row's home goes with it");
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(personBefore, "the person's folder: the same names, bytes and read-only bits as before");
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData("answered")]
    [InlineData("refused at the copy")]
    [InlineData("refused by the launch control")]
    [InlineData("cancelled mid-turn")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        var before = _person.Hash();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var kimi = new PlayedKimi(_launcher)
        {
            VersionExit = ending == "refused by the launch control" ? 1 : 0,
            Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 100, 5, Advice),
            During = async (_, ct) =>
            {
                if (ending == "cancelled mid-turn")
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };
        var granted = Path.Combine(Repo, "src");
        var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
        var service = Service(kimi, ending == "refused at the copy" ? plain : granted);
        var refused = ending.StartsWith("refused", StringComparison.Ordinal);

        if (ending == "cancelled mid-turn")
        {
            var asking = async () => await AskAsync(service, caller.Token);
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var row = TheRow(await AskAsync(service));
            row.GetProperty("status").GetString().Should().Be(ending switch { "answered" => RowOutcomes.Answered, "refused at the copy" => RowOutcomes.Refused, _ => RowOutcomes.Failed }, row.ToString());
        }

        if (ending == "refused by the launch control")
        {
            // The refusal comes AFTER the owned home and its junction were made: `--version` ran in that home and failed.
            kimi.VersionHomesThere.Should().ContainSingle().Which.Should().BeTrue("the home was made before the launch control asked the version");
            Directory.Exists(kimi.Versions.Single().WorkingDirectory).Should().BeFalse("the refused row's home goes with it");
        }

        var shim = kimi.Shims.SingleOrDefault()?.Request;
        (shim is null).Should().Be(refused, "a refused row never reaches the shim");
        if (shim is not null)
        {
            var copy = After(shim, "--cwd");
            shim.StdIn.Should().Contain(copy, "the prompt names the copy Kimi reads").And.NotContain(granted, "never the original root (D6)");
            shim.Arguments.Should().NotContain(argument => argument.Contains(granted, StringComparison.Ordinal));
        }

        NothingOfTheRowIsLeft(shim, before);
    }
}
