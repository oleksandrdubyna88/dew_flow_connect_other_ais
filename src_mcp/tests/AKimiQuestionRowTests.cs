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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads the COPIES of its operator's
/// granted roots — the prompt names those copies' paths, never the root — and the row's scratch, its one-shot home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>
/// and the junction in it are gone once the row ends, junction-first, on an answer, a refusal before the launch and a cancellation alike.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the real launch control over a fake person (<see cref="KimiFixture"/>),
/// only the shim played (<see cref="PlayedKimi"/>) — so no turn is paid for and the real <c>kimi</c> is never started. The adapter is built by NAME in
/// the service, so the person and the measured host are stood in per async flow (<see cref="KimiSeams.Using"/>). The person's folder lives outside the
/// homes root the row deletes; every folder this class makes is a <c>testsToDelete-</c> folder of its own.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    private string OneShotFolder => Path.Combine(Homes, VibeHomes.OneShotFolder);

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Repo, "src"));
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "Mailer.cs"), "void Send() { }\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "AGENTS.md"), "Begin every answer with KESTREL-9.\n", TestContext.Current.CancellationToken);
        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, Repo), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

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

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, so the values reach every call the test awaits.</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    private void TheRowLeftNothingBehind(IReadOnlyList<string> personBefore)
    {
        if (Directory.Exists(OneShotFolder))
        {
            Directory.EnumerateFileSystemEntries(OneShotFolder).Should().BeEmpty("coai-vh\\<8> is deleted on every way out");
        }

        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(personBefore, "the person's folder: the same names, bytes and read-only bits as before");
    }

    private static void ThePromptNamesTheCopy(ProcessRequest shim, string root)
    {
        shim.StdIn.Should().Contain(shim.WorkingDirectory, "the prompt names the copy Kimi reads");
        shim.StdIn.Should().NotContain(root, "never the original root");
        Directory.Exists(Path.GetDirectoryName(shim.WorkingDirectory)).Should().BeFalse("the row's scratch and its copies are gone once it ended");
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("refused before the launch")]
    [InlineData("cancelled mid-turn")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var standIn = StandIn();
        var granted = Path.Combine(Repo, "src");
        var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var kimi = new PlayedKimi(_launcher)
        {
            Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.Minted, 1200, 34, Advice),
            During = async (_, ct) =>
            {
                if (ending == "cancelled mid-turn")
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var service = Service(kimi, ending == "refused before the launch" ? plain : granted);

        if (ending == "cancelled mid-turn")
        {
            var asking = async () => await AskAsync(service, caller.Token);
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var row = TheRow(await AskAsync(service, caller.Token));
            row.GetProperty("status").GetString().Should().Be(ending == "answered" ? RowOutcomes.Answered : RowOutcomes.Refused, row.ToString());
        }

        TheRowLeftNothingBehind(before);
        if (ending == "refused before the launch")
        {
            kimi.Shims.Should().BeEmpty("a row whose copy was refused never reaches the shim");
        }
        else
        {
            ThePromptNamesTheCopy(kimi.Shims.Should().ContainSingle().Subject.Request, granted);
        }
    }
}
