using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots —
/// the prompt names the copies' paths, never the roots — and the row's one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) and its scratch are gone,
/// junction-first, once the row ends: on an answer, a refusal before the launch and a cancellation in flight alike.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the real launch control over a fake person (<see cref="KimiFixture"/>, stood in
/// by <see cref="AKimiConsultationScenarioTests.StandIn"/>), only the shim played (<see cref="PlayedKimi"/>). The model is
/// <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    /// <summary>The homes root, inside the scenario's folder: a home-keeping row reserves its one-shot folder there (D1).</summary>
    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>A git repository at <paramref name="at"/> holding the files, committed — as the Vibe row test makes its roots.</summary>
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

    /// <summary>The service with one Kimi <c>question-disk</c> row over <paramref name="roots"/>, the shim played by <paramref name="kimi"/>, the homes root set.</summary>
    private PanelService Service(PlayedKimi kimi, params string[] roots)
    {
        return new PanelService(
            new PanelSettings
            {
                ConsultantHomesRoot = Homes,
                Providers = [],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _dir.At("data"),
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                QuestionConsult = new QuestionConsultSettings
                {
                    Rows = [new QuestionRow("kimi-disk", "moonshot", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
                    Roots = roots,
                    RowBudget = TimeSpan.FromSeconds(30),
                },
            },
            VaultKeys.None("no vault in tests"),
            default,
            kimi,
            Logger.None,
            Noticing.None);
    }

    /// <summary>One <c>ask_consultants</c> call over the repository, as the Vibe row test makes it.</summary>
    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null)
    {
        return JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData("answered")]
    [InlineData("refused before the launch")]
    [InlineData("cancelled mid-turn")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        ending.Should().NotBeNullOrEmpty("the arm's name");
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        var before = _person.Hash();
        var granted = Path.Combine(Repo, "src");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var kimi = new PlayedKimi(_launcher);
        string[] roots = [granted];
        if (ending == "refused before the launch")
        {
            var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
            await File.WriteAllTextAsync(Path.Combine(plain, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
            roots = [plain];
        }
        else if (ending == "cancelled mid-turn")
        {
            kimi = new PlayedKimi(_launcher)
            {
                During = async (_, ct) =>
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                },
            };
        }

        var asking = () => AskAsync(Service(kimi, roots), caller.Token);
        if (ending == "cancelled mid-turn")
        {
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await asking();
            var row = reply.GetProperty("answers").EnumerateArray().Single();
            var status = row.GetProperty("status").GetString();
            if (ending == "answered")
            {
                status.Should().Be(RowOutcomes.Answered, reply.ToString());
                var request = kimi.Shims.Should().ContainSingle().Subject.Request;
                var cwd = request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];
                request.StdIn.Should().MatchRegex(System.Text.RegularExpressions.Regex.Escape(Path.Combine(cwd, "snapshot-")) + "[0-9a-f]+", "the prompt names the copy of the granted folder, which is under the working folder - not the working folder alone");
                request.StdIn.Should().NotContain(granted, "never the original, which it cannot read");
            }
            else
            {
                status.Should().Be(RowOutcomes.Refused, reply.ToString());
                row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree");
                kimi.Shims.Should().BeEmpty();
                kimi.Versions.Should().BeEmpty();
            }
        }

        foreach (var launch in kimi.Shims)
        {
            var args = launch.Request.Arguments.ToList();
            var home = args[args.IndexOf("--home") + 1];
            Directory.Exists(Path.GetDirectoryName(home)).Should().BeFalse("the row's one-shot folder is gone after the row ends");
        }

        var oneShot = Path.Combine(Homes, VibeHomes.OneShotFolder);
        if (Directory.Exists(oneShot))
        {
            Directory.EnumerateFileSystemEntries(oneShot).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        }

        _unlinkedByTheDeleter.Should().BeEmpty();
        _person.Hash().Should().Equal(before);
    }
}
